#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2024 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ShareX
{
    public class WorkerTask : IDisposable
    {
        public delegate void TaskEventHandler(WorkerTask task);
        public delegate void TaskImageEventHandler(WorkerTask task, Bitmap image);
     
        public event TaskEventHandler StatusChanged, UploadStarted, UploadProgressChanged, UploadCompleted, TaskCompleted;
        public event TaskImageEventHandler ImageReady;
    
        public TaskInfo Info { get; private set; }
        public TaskStatus Status { get; private set; }
        public bool IsBusy => Status == TaskStatus.InQueue || IsWorking;
        public bool IsWorking => Status == TaskStatus.Preparing || Status == TaskStatus.Working || Status == TaskStatus.Stopping;
        public bool StopRequested { get; private set; }
        public bool RequestSettingUpdate { get; private set; }
        public bool EarlyURLCopied { get; private set; }
        public Stream Data { get; private set; }
        public Bitmap Image { get; private set; }
        public bool KeepImage { get; set; }
        public string Text { get; private set; }

        private ThreadWorker threadWorker;
     
        #region Constructors

        private WorkerTask(TaskSettings taskSettings)
        {
            Status = TaskStatus.InQueue;
            Info = new TaskInfo(taskSettings);
        }

        public static WorkerTask CreateHistoryTask(RecentTask recentTask)
        {
            WorkerTask task = new WorkerTask(null);
            task.Status = TaskStatus.History;
            task.Info.FilePath = recentTask.FilePath;
            task.Info.FileName = recentTask.FileName;

            task.Info.TaskEndTime = recentTask.Time;

            return task;
        }

        public static WorkerTask CreateDataUploaderTask(EDataType dataType, Stream stream, string fileName, TaskSettings taskSettings)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.Job = TaskJob.DataUpload;
            task.Info.DataType = dataType;
            task.Info.FileName = fileName;
            task.Data = stream;
            return task;
        }

        public static WorkerTask CreateFileUploaderTask(string filePath, TaskSettings taskSettings)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.FilePath = filePath;
            task.Info.DataType = TaskHelpers.FindDataType(task.Info.FilePath, taskSettings);

            if (task.Info.TaskSettings.UploadSettings.FileUploadUseNamePattern)
            {
                string ext = FileHelpers.GetFileNameExtension(task.Info.FilePath);
                task.Info.FileName = TaskHelpers.GetFileName(task.Info.TaskSettings, ext);
            }

            if (task.Info.TaskSettings.AdvancedSettings.ProcessImagesDuringFileUpload && task.Info.DataType == EDataType.Image)
            {
                task.Info.Job = TaskJob.Job;
                task.Image = ImageHelpers.LoadImage(task.Info.FilePath);
            }
            else
            {
                task.Info.Job = TaskJob.FileUpload;

                if (!task.LoadFileStream())
                {
                    return null;
                }
            }

            return task;
        }

        public static WorkerTask CreateImageUploaderTask(TaskMetadata metadata, TaskSettings taskSettings, string customFileName = null)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.Job = TaskJob.Job;
            task.Info.DataType = EDataType.Image;

            if (!string.IsNullOrEmpty(customFileName))
            {
                task.Info.FileName = FileHelpers.AppendExtension(customFileName, "bmp");
            }
            else
            {
                task.Info.FileName = TaskHelpers.GetFileName(taskSettings, "bmp", metadata);
            }

            task.Info.Metadata = metadata;
            task.Image = metadata.Image;
            return task;
        }

        public static WorkerTask CreateTextUploaderTask(string text, TaskSettings taskSettings)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.Job = TaskJob.TextUpload;
            task.Info.DataType = EDataType.Text;
            task.Info.FileName = TaskHelpers.GetFileName(taskSettings, taskSettings.AdvancedSettings.TextFileExtension);
            task.Text = text;
            return task;
        }

        public static WorkerTask CreateFileJobTask(string filePath, TaskMetadata metadata, TaskSettings taskSettings, string customFileName = null)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.FilePath = filePath;
            task.Info.DataType = TaskHelpers.FindDataType(task.Info.FilePath, taskSettings);

            if (!string.IsNullOrEmpty(customFileName))
            {
                string ext = FileHelpers.GetFileNameExtension(task.Info.FilePath);
                task.Info.FileName = FileHelpers.AppendExtension(customFileName, ext);
            }
            else if (task.Info.TaskSettings.UploadSettings.FileUploadUseNamePattern)
            {
                string ext = FileHelpers.GetFileNameExtension(task.Info.FilePath);
                task.Info.FileName = TaskHelpers.GetFileName(task.Info.TaskSettings, ext);
            }

            task.Info.Metadata = metadata;
            task.Info.Job = TaskJob.Job;

            if (task.Info.IsUploadJob && !task.LoadFileStream())
            {
                return null;
            }

            return task;
        }

        public static WorkerTask CreateDownloadTask(string url, bool upload, TaskSettings taskSettings)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.Job = upload ? TaskJob.DownloadUpload : TaskJob.Download;

            string fileName = URLHelpers.URLDecode(url, 10);
            fileName = URLHelpers.GetFileName(fileName);
            fileName = FileHelpers.SanitizeFileName(fileName);

            if (task.Info.TaskSettings.UploadSettings.FileUploadUseNamePattern)
            {
                string ext = FileHelpers.GetFileNameExtension(fileName);
                fileName = TaskHelpers.GetFileName(task.Info.TaskSettings, ext);
            }

            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            task.Info.FileName = fileName;
            task.Info.DataType = TaskHelpers.FindDataType(task.Info.FileName, taskSettings);
            return task;
        }

        #endregion Constructors

        public void Start()
        {
            if (Status == TaskStatus.InQueue && !StopRequested)
            {
                Info.TaskStartTime = DateTime.Now;

                threadWorker = new ThreadWorker();
                Prepare();
                threadWorker.DoWork += ThreadDoWork;
                threadWorker.Completed += ThreadCompleted;
                threadWorker.Start(ApartmentState.STA);
            }
        }

        private void Prepare()
        {
            Status = TaskStatus.Preparing;

            switch (Info.Job)
            {
                case TaskJob.Job:
                case TaskJob.TextUpload:
                    Info.Status = Resources.UploadTask_Prepare_Preparing;
                    break;
                default:
                    Info.Status = Resources.UploadTask_Prepare_Starting;
                    break;
            }

            OnStatusChanged();
        }

        public void Stop()
        {
            StopRequested = true;

            switch (Status)
            {
                case TaskStatus.InQueue:
                    OnTaskCompleted();
                    break;
                case TaskStatus.Preparing:
                case TaskStatus.Working:
                    
                    Status = TaskStatus.Stopping;
                    Info.Status = Resources.UploadTask_Stop_Stopping;
                    OnStatusChanged();
                    break;
            }
        }

        public void ShowErrorWindow()
        {
            
        }

        private void ThreadDoWork()
        {
            CreateTaskReferenceHelper();

            try
            {
                StopRequested = !DoThreadJob();

                OnImageReady();

                if (!StopRequested)
                {
                    if (Info.IsUploadJob && TaskHelpers.IsUploadAllowed())
                    {
                        DoUploadJob();
                    }
                    
                }
            }
            finally
            {
                KeepImage = Image != null && Info.TaskSettings.GeneralSettings.ShowToastNotificationAfterTaskCompleted;

                Dispose();

       

                if ((Info.Job == TaskJob.Job || (Info.Job == TaskJob.FileUpload && Info.TaskSettings.AdvancedSettings.UseAfterCaptureTasksDuringFileUpload))
                    && Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DeleteFile) && !string.IsNullOrEmpty(Info.FilePath) && File.Exists(Info.FilePath))
                {
                    File.Delete(Info.FilePath);
                }
            }

     
        }

        private void CreateTaskReferenceHelper()
        {
            
        }

        private void DoUploadJob()
        {
            if (Program.Settings.ShowUploadWarning)
            {
                bool disableUpload = !FirstTimeUploadForm.ShowForm();

                Program.Settings.ShowUploadWarning = false;

                if (disableUpload)
                {
                    //Program.DefaultTaskSettings.AfterCaptureJob = Program.DefaultTaskSettings.AfterCaptureJob.Remove(AfterCaptureTasks.UploadImageToHost);

                    foreach (HotkeySettings hotkeySettings in Program.HotkeysConfig.Hotkeys)
                    {
                        if (hotkeySettings.TaskSettings != null)
                        {
                           // hotkeySettings.TaskSettings.AfterCaptureJob = hotkeySettings.TaskSettings.AfterCaptureJob.Remove(AfterCaptureTasks.UploadImageToHost);
                        }
                    }

        
                    RequestSettingUpdate = true;

                    return;
                }
            }

            if (Program.Settings.ShowLargeFileSizeWarning > 0)
            {
                long dataSize = Program.Settings.BinaryUnits ? Program.Settings.ShowLargeFileSizeWarning * 1024 * 1024 : Program.Settings.ShowLargeFileSizeWarning * 1000 * 1000;
                if (Data != null && Data.Length > dataSize)
                {
                    using (MyMessageBox msgbox = new MyMessageBox(Resources.UploadTask_DoUploadJob_You_are_attempting_to_upload_a_large_file, "ShareX",
                        MessageBoxButtons.YesNo, Resources.UploadManager_IsUploadConfirmed_Don_t_show_this_message_again_))
                    {
                        msgbox.ShowDialog();
                        if (msgbox.IsChecked) Program.Settings.ShowLargeFileSizeWarning = 0;
                        if (msgbox.DialogResult == DialogResult.No) Stop();
                    }
                }
            }

            if (!StopRequested)
            {
                SettingManager.WaitUploadersConfig();

                Status = TaskStatus.Working;
                Info.Status = Resources.UploadTask_DoUploadJob_Uploading;

                TaskbarManager.SetProgressState(Program.MainForm, TaskbarProgressBarStatus.Normal);

                bool cancelUpload = false;

                //if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowBeforeUploadWindow))
                //{
                //    using (BeforeUploadForm form = new BeforeUploadForm(Info))
                //    {
                //        cancelUpload = form.ShowDialog() != DialogResult.OK;
                //    }
                //}

                if (!cancelUpload)
                {
                    OnUploadStarted();

                    bool isError = DoUpload(Data, Info.FileName);

                    if (isError && Program.Settings.MaxUploadFailRetry > 0)
                    {
                        for (int retry = 1; !StopRequested && isError && retry <= Program.Settings.MaxUploadFailRetry; retry++)
                        {
                            DebugHelper.WriteLine("Upload failed. Retrying upload.");
                            isError = DoUpload(Data, Info.FileName, retry);
                        }
                    }

                    if (!isError)
                    {
                        OnUploadCompleted();
                    }
                }
               
            }
        }

        private bool DoUpload(Stream data, string fileName, int retry = 0)
        {
            

            return true;
        }



        private bool DoThreadJob()
        {
            if (Info.IsUploadJob && Info.TaskSettings.AdvancedSettings.AutoClearClipboard)
            {
                ClipboardHelpers.Clear();
            }

            if (Info.Job == TaskJob.Download || Info.Job == TaskJob.DownloadUpload)
            {
                if (Info.Job == TaskJob.Download)
                {
                    return true;
                }
            }

            if (Info.Job == TaskJob.Job)
            {
                if (!DoAfterCaptureJobs())
                {
                    return false;
                }

                DoFileJobs();
            }
            else if (Info.Job == TaskJob.TextUpload && !string.IsNullOrEmpty(Text))
            {
                DoTextJobs();
            }
            else if (Info.Job == TaskJob.FileUpload && Info.TaskSettings.AdvancedSettings.UseAfterCaptureTasksDuringFileUpload)
            {
                DoFileJobs();
            }

            //if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DoOCR))
            //{
            //    DoOCR();
            //}

            if (Info.IsUploadJob && Data != null && Data.CanSeek)
            {
                Data.Position = 0;
            }

            return true;
        }

        private bool DoAfterCaptureJobs()
        {
            if (Image == null)
            {
                return true;
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.BeautifyImage))
            {
                Image = TaskHelpers.BeautifyImage(Image, Info.TaskSettings);

                if (Image == null)
                {
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AddImageEffects))
            {
                Image = TaskHelpers.ApplyImageEffects(Image, Info.TaskSettings.ImageSettingsReference);

                if (Image == null)
                {
                    DebugHelper.WriteLine("Error: Applying image effects resulted empty image.");
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AnnotateImage))
            {
                Image = TaskHelpers.AnnotateImage(Image, null, Info.TaskSettings, true);

                if (Image == null)
                {
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyImageToClipboard))
            {
                ClipboardHelpers.CopyImage(Image, Info.FileName);
                DebugHelper.WriteLine("Image copied to clipboard.");
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PinToScreen))
            {
                Image imageCopy = Image.CloneSafe();
                TaskHelpers.PinToScreen(imageCopy, Info.TaskSettings.ToolsSettingsReference.PinToScreenOptions);
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SendImageToPrinter))
            {
                TaskHelpers.PrintImage(Image);
            }

            Info.Metadata.Image = Image;

            if (Info.TaskSettings.AfterCaptureJob.HasFlagAny(AfterCaptureTasks.SaveImageToFile, AfterCaptureTasks.SaveImageToFileWithDialog))
            {
                ImageData imageData = TaskHelpers.PrepareImage(Image, Info.TaskSettings);
                Data = imageData.ImageStream;
                Info.FileName = Path.ChangeExtension(Info.FileName, imageData.ImageFormat.GetDescription());

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFile))
                {
                    string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                    string filePath = TaskHelpers.HandleExistsFile(screenshotsFolder, Info.FileName, Info.TaskSettings);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        Info.FilePath = filePath;
                        imageData.Write(Info.FilePath);
                        DebugHelper.WriteLine("Image saved to file: " + Info.FilePath);
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFileWithDialog))
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        string initialDirectory = null;

                        if (!string.IsNullOrEmpty(HelpersOptions.LastSaveDirectory) && Directory.Exists(HelpersOptions.LastSaveDirectory))
                        {
                            initialDirectory = HelpersOptions.LastSaveDirectory;
                        }
                        else
                        {
                            initialDirectory = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                        }

                        bool imageSaved;

                        do
                        {
                            sfd.InitialDirectory = initialDirectory;
                            sfd.FileName = Info.FileName;
                            sfd.DefaultExt = Path.GetExtension(Info.FileName).Substring(1);
                            sfd.Filter = string.Format("*{0}|*{0}|All files (*.*)|*.*", Path.GetExtension(Info.FileName));
                            sfd.Title = Resources.UploadTask_DoAfterCaptureJobs_Choose_a_folder_to_save + " " + Path.GetFileName(Info.FileName);

                            if (sfd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(sfd.FileName))
                            {
                                Info.FilePath = sfd.FileName;
                                HelpersOptions.LastSaveDirectory = Path.GetDirectoryName(Info.FilePath);
                                imageSaved = imageData.Write(Info.FilePath);

                                if (imageSaved)
                                {
                                    DebugHelper.WriteLine("Image saved to file with dialog: " + Info.FilePath);
                                }
                            }
                            else
                            {
                                break;
                            }
                        } while (!imageSaved);
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveThumbnailImageToFile))
                {
                    string thumbnailFileName, thumbnailFolder;

                    if (!string.IsNullOrEmpty(Info.FilePath))
                    {
                        thumbnailFileName = Path.GetFileName(Info.FilePath);
                        thumbnailFolder = Path.GetDirectoryName(Info.FilePath);
                    }
                    else
                    {
                        thumbnailFileName = Info.FileName;
                        thumbnailFolder = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                    }

                    Info.ThumbnailFilePath = TaskHelpers.CreateThumbnail(Image, thumbnailFolder, thumbnailFileName, Info.TaskSettings);

                    if (!string.IsNullOrEmpty(Info.ThumbnailFilePath))
                    {
                        DebugHelper.WriteLine("Thumbnail saved to file: " + Info.ThumbnailFilePath);
                    }
                }
            }

            return true;
        }

        private void DoFileJobs()
        {
            if (!string.IsNullOrEmpty(Info.FilePath) && File.Exists(Info.FilePath))
            {
                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PerformActions) && Info.TaskSettings.ExternalPrograms != null)
                {
                    IEnumerable<ExternalProgram> actions = Info.TaskSettings.ExternalPrograms.Where(x => x.IsActive);

                    if (actions.Count() > 0)
                    {
                        bool isFileModified = false;
                        string fileName = Info.FileName;

                        foreach (ExternalProgram fileAction in actions)
                        {
                            string modifiedPath = fileAction.Run(Info.FilePath);

                            if (!string.IsNullOrEmpty(modifiedPath))
                            {
                                isFileModified = true;
                                Info.FilePath = modifiedPath;

                                if (Data != null)
                                {
                                    Data.Dispose();
                                }

                                fileAction.DeletePendingInputFile();
                            }
                        }

                        if (isFileModified)
                        {
                            string extension = FileHelpers.GetFileNameExtension(Info.FilePath);
                            Info.FileName = FileHelpers.ChangeFileNameExtension(fileName, extension);

                            LoadFileStream();
                        }
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFileToClipboard))
                {
                    ClipboardHelpers.CopyFile(Info.FilePath);
                }
                else if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFilePathToClipboard))
                {
                    ClipboardHelpers.CopyText(Info.FilePath);
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowInExplorer))
                {
                    FileHelpers.OpenFolderWithFile(Info.FilePath);
                }

                //if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ScanQRCode) && Info.DataType == EDataType.Image)
                //{
                //    QRCodeForm.OpenFormScanFromImageFile(Info.FilePath).ShowDialog();
                //}
            }
        }

        private void DoTextJobs()
        {
            if (Info.TaskSettings.AdvancedSettings.TextTaskSaveAsFile)
            {
                string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings);
                string filePath = TaskHelpers.HandleExistsFile(screenshotsFolder, Info.FileName, Info.TaskSettings);

                if (!string.IsNullOrEmpty(filePath))
                {
                    Info.FilePath = filePath;
                    FileHelpers.CreateDirectoryFromFilePath(Info.FilePath);
                    File.WriteAllText(Info.FilePath, Text, Encoding.UTF8);
                    DebugHelper.WriteLine("Text saved to file: " + Info.FilePath);
                }
            }

            byte[] byteArray = Encoding.UTF8.GetBytes(Text);
            Data = new MemoryStream(byteArray);
        }


        private void DoOCR()
        {
            if (Image != null && Info.DataType == EDataType.Image)
            {
                TaskHelpers.OCRImage(Image, Info.TaskSettings).GetAwaiter().GetResult();
            }
        }

        private bool LoadFileStream()
        {
            try
            {
                Data = new FileStream(Info.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception e)
            {
                e.ShowError();
                return false;
            }

            return true;
        }

        private void ThreadCompleted()
        {
            OnTaskCompleted();
        }



        private void OnStatusChanged()
        {
            if (StatusChanged != null)
            {
                threadWorker.InvokeAsync(() => StatusChanged(this));
            }
        }

        private void OnImageReady()
        {
            if (ImageReady != null)
            {
                Bitmap image = null;

                if (Program.Settings.TaskViewMode == TaskViewMode.ThumbnailView && Image != null)
                {
                    image = (Bitmap)Image.Clone();
                }

                threadWorker.InvokeAsync(() =>
                {
                    using (image)
                    {
                        ImageReady(this, image);
                    }
                });
            }
        }

        private void OnUploadStarted()
        {
            if (UploadStarted != null)
            {
                threadWorker.InvokeAsync(() => UploadStarted(this));
            }
        }

        private void OnUploadCompleted()
        {
            if (UploadCompleted != null)
            {
                threadWorker.InvokeAsync(() => UploadCompleted(this));
            }
        }

        private void OnUploadProgressChanged()
        {
            if (UploadProgressChanged != null)
            {
                threadWorker.InvokeAsync(() => UploadProgressChanged(this));
            }
        }

        private void OnTaskCompleted()
        {
            Info.TaskEndTime = DateTime.Now;

            if (StopRequested)
            {
                Status = TaskStatus.Stopped;
                Info.Status = Resources.UploadTask_OnUploadCompleted_Stopped;
            }
            else 
            {
                Status = TaskStatus.Completed;
                Info.Status = Resources.UploadTask_OnUploadCompleted_Done;
            }

            TaskCompleted?.Invoke(this);

            Dispose();
        }

        public void Dispose()
        {
            if (Data != null)
            {
                Data.Dispose();
                Data = null;
            }

            if (!KeepImage && Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
}