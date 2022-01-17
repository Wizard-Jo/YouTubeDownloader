using System;
using System.Threading;
using System.Diagnostics;
using System.IO;

using System.Linq;
using System.Text.RegularExpressions;

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Microsoft.WindowsAPICodePack.Dialogs;

using VideoLibrary;
using VideoLibrary.Debug;
using Path = System.IO.Path;

namespace YouTubeDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string outputPath = GetDefaultFolder();

        public MainWindow()
        {
            InitializeComponent();
            outputPath = GetDefaultFolder();
            PathBox.Text = outputPath;

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            ComboBoxItem item = ResolutionBox.SelectedItem as ComboBoxItem;
            string resName = item.Content.ToString();
            int resolution = Convert.ToInt32(resName.Remove(resName.Length - 1));

            new Thread(Download).Start();

        }

        protected async void Download()
        {
            try
            {
                Stopwatch sw = new Stopwatch();

                //get video
                string link = "";
                int resolution = 0;
                string progress;
                int frameCount = 0;

                //Write info
                this.Dispatcher.Invoke((Action)(() =>
                {
                    link = LinkBox.Text;

                    ComboBoxItem item = ResolutionBox.SelectedItem as ComboBoxItem;
                    string resName = item.Content.ToString();
                    resolution = Convert.ToInt32(resName.Remove(resName.Length - 1));

                    OutputText.Foreground = Brushes.DarkGoldenrod;
                    OutputText.Text = $"Starting download";
                    Mouse.OverrideCursor = Cursors.Wait;
                    button.IsEnabled = false;
                }));

                sw.Start();

                //Info
                var youtube = new CustomYouTube();
                var videos = await youtube.GetAllVideosAsync(link);
                var targetVid = videos.First(i => i.Resolution == resolution);
                string tempPath = Path.GetTempPath();

                frameCount = Convert.ToInt32(targetVid.Fps * targetVid.Info.LengthSeconds);
                Debug.WriteLine($"FPS: {targetVid.Fps} Duration: {targetVid.Info.LengthSeconds}sec Framecount: {Convert.ToInt32(targetVid.Fps * targetVid.Info.LengthSeconds)}");

                //Download video in chunks
                youtube.CreateDownloadAsync(
                    new Uri(targetVid.Uri),
                    Path.Combine(tempPath,
                    targetVid.FullName),
                    new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
                    {
                        var percent = (int)((v.Item1 * 100) / v.Item2);
                        progress = string.Format("Downloading video.. ( % {0} ) {1} / {2} MB\r", percent, (v.Item1 / (double)(1024 * 1024)).ToString("N"), (v.Item2 / (double)(1024 * 1024)).ToString("N"));

                        this.Dispatcher.Invoke((Action)(() =>
                        {
                            OutputText.Text = $"{progress}";

                        }));

                    })).GetAwaiter().GetResult();


                string videoPath = Path.Combine(tempPath, targetVid.FullName);
                Debug.WriteLine($"Video written to {videoPath}");


                //Get audio
                var audios = videos.Where(_ => _.AudioFormat == AudioFormat.Aac && _.AdaptiveKind == AdaptiveKind.Audio).ToList();
                var mpAudio = audios.FirstOrDefault(x => x.AudioBitrate > 0);

                string audioPath = Path.Combine(tempPath, mpAudio.FullName + ".mp3");

                //Download audio
                youtube.CreateDownloadAsync(
                   new Uri(mpAudio.Uri),
                   audioPath,
                   new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
                   {
                       var percent = (int)((v.Item1 * 100) / v.Item2);
                       progress = string.Format("Downloading audio.. ( % {0} ) {1} / {2} MB\r", percent, (v.Item1 / (double)(1024 * 1024)).ToString("N"), (v.Item2 / (double)(1024 * 1024)).ToString("N"));

                       this.Dispatcher.Invoke((Action)(() =>
                       {
                           OutputText.Text = $"{progress}";

                       }));

                   })).GetAwaiter().GetResult();

                Debug.WriteLine($"Audio written to {audioPath}");


                // Combine audio and video with ffmpeg
                this.Dispatcher.Invoke((Action)(() =>
                {
                    OutputText.Text = $"Starting reencode";
                }));

                if (targetVid.FileExtension == ".webm")
                {
                    Debug.WriteLine($"Changing file extension from {targetVid.FileExtension} to .mp4");
                }

                string finalOutputPath = Path.Combine(outputPath, targetVid.FullName);
                finalOutputPath = finalOutputPath.Replace(targetVid.FileExtension, ".mp4");

                await ReencodeVideo(videoPath, audioPath, finalOutputPath, frameCount);

                sw.Stop();

                this.Dispatcher.Invoke((Action)(() =>
                {
                    TimeSpan ts = sw.Elapsed;
                    string elapsedTime = String.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

                    OutputText.Foreground = Brushes.Green;
                    OutputText.Text = $"Video encoded in {elapsedTime} to {outputPath}";
                    Mouse.OverrideCursor = null;
                    button.IsEnabled = true;
                }));

                //Deletes temp files
                File.Delete(videoPath);
                File.Delete(audioPath);

            }
            catch (ArgumentException)
            {
                this.Dispatcher.Invoke((Action)(() =>
                {
                    OutputText.Foreground = Brushes.Red;
                    OutputText.Text = $"Link is not a valid youtube URL";
                    button.IsEnabled = true;
                }));

            }
            catch (NullReferenceException)
            {
                this.Dispatcher.Invoke((Action)(() =>
                {
                    OutputText.Foreground = Brushes.Red;
                    OutputText.Text = $"Video couldn't be downloaded";
                    button.IsEnabled = true;
                }));

            }
        }


        private async Task ReencodeVideo(string video, string audio, string output, int frameCount)
        {
            try
            {
                string args = @$"-y -i ""{video}"" -i ""{audio}"" -c:v copy -c:a aac ""{output}""";


                Process ffmpegInstance = new Process();

                ffmpegInstance.StartInfo.UseShellExecute = false;
                ffmpegInstance.StartInfo.Arguments = args;
                ffmpegInstance.StartInfo.CreateNoWindow = true;
                ffmpegInstance.StartInfo.FileName = @"ffmpeg.exe";
                ffmpegInstance.StartInfo.RedirectStandardError = true;

                ffmpegInstance.ErrorDataReceived += (sender, args) => Display(args.Data, frameCount);

                ffmpegInstance.Start();
                ffmpegInstance.BeginErrorReadLine();

                ffmpegInstance.WaitForExit();
            }
            catch
            {
                this.Dispatcher.Invoke((Action)(() =>
                {
                    OutputText.Foreground = Brushes.Red;
                    OutputText.Text = $"FFmpeg error - FFmpeg could not be found";
                    button.IsEnabled = true;
                }));

                //Deletes temp files
                File.Delete(video);
                File.Delete(audio);

            }
        }

        private void Display(string output, int frameCount)
        {
            Regex rx = new Regex(@"(?<=frame+=+\s?)\d+");

            Match m;

            if (output != null)
                if (rx.IsMatch(output))
                {
                    m = rx.Match(output);

                    float percentage = (float)(Math.Round((Convert.ToDouble(m.Value) / (float)frameCount) * 100, 2));

                    Debug.WriteLine($"ffmpeg: {output}");

                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        if (percentage < 99)
                        {
                            OutputText.Text = $"Reencoding in progress: ( % {percentage} ) - Frame {m.Value} out of {frameCount}";
                        }
                        else
                        {
                            OutputText.Text = $"Finishing reencode...";
                        }


                    }));
                }

        }

        private void LinkBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        static string GetDefaultFolder()
        {
            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

            return Path.Combine(home, "Downloads");
        }

        private void ResolutionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem item = ResolutionBox.SelectedItem as ComboBoxItem;
            Debug.WriteLine($"{item.Content.ToString().Remove(item.Content.ToString().Length - 1)}p selected");
        }

        private void ChooseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = GetDefaultFolder();
            dialog.IsFolderPicker = true;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                outputPath = dialog.FileName;
                PathBox.Text = outputPath;
            }
        }
    }
}
