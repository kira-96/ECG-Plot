using Dicom;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ECG_Plot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Logging.ILoggerService logger = Logging.LoggerService.Instance;

        private readonly WriteableBitmap chartBitmap;

        readonly int bitmapWidth = 1201;
        readonly int bitmapHeight = 721;

        /// <summary>
        /// max data
        /// </summary>
        readonly int maxData = 1000;

        /// <summary>
        /// grid rows
        /// </summary>
        readonly int rows = 24;

        /// <summary>
        /// cell grids
        /// </summary>
        readonly int ticks = 5;

        /// <summary>
        /// grid columns
        /// </summary>
        int columns;

        /// <summary>
        /// cell size
        /// </summary>
        int cellSize;

        /// <summary>
        /// spacing between lines
        /// </summary>
        int spacing;

        /// <summary>
        /// Lead
        /// <see cref="Lead" />
        /// </summary>
        Lead CurrentLead = Lead.Regular;

        /// <summary>
        /// has ECG data
        /// </summary>
        bool hasData = false;

        /// <summary>
        /// data
        /// </summary>
        short[,] waveformData;

        public MainWindow()
        {
            InitializeComponent();

            chartBitmap = new WriteableBitmap(bitmapWidth, bitmapHeight, 96.0, 96.0, PixelFormats.Bgr24, null);
            ChartImage.Source = chartBitmap;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CalculateCellSize(chartBitmap.PixelWidth, chartBitmap.PixelHeight);

            Redraw(chartBitmap.PixelWidth, chartBitmap.PixelHeight);
        }

        private void OnDragOver(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Link;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private async void OnDrop(object s, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string path = ((Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();

            if (!File.Exists(path))
                return;

            await OpenDcmFile(path);
        }

        private async void OnOpenClick(object s, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                Filter = "Dicom Files (*.dcm)|*.dcm",
            };

            if (dialog.ShowDialog() != true)
                return;

            await OpenDcmFile(dialog.FileName);
        }

        private void OnExitClick(object s, RoutedEventArgs e)
        {
            Close();
        }

        private void OnLayoutChanged(object s, RoutedEventArgs e)
        {
            MenuItem clickedItem = s as MenuItem;

            if (!clickedItem.IsChecked)
            {
                clickedItem.IsChecked = true;
                return;
            }

            MenuItem parent = clickedItem.Parent as MenuItem;

            foreach (var item in parent.Items)
            {
                if (item == s)
                    continue;

                (item as MenuItem).IsChecked = false;
            }

            switch (clickedItem.Tag)
            {
                case "Regular":
                    CurrentLead = Lead.Regular;
                    break;
                case "3×4":
                    CurrentLead = Lead.L3_4;
                    break;
                case "3×4+1":
                    CurrentLead = Lead.L3_4_1;
                    break;
                case "3×4+3":
                    CurrentLead = Lead.L3_4_3;
                    break;
                case "6×2":
                    CurrentLead = Lead.L6_2;
                    break;
                case "Average Complex":
                    CurrentLead = Lead.AverageComplex;
                    break;
            }

            // change layout redraw
            Redraw(chartBitmap.PixelWidth, chartBitmap.PixelHeight);
        }

        public async Task OpenDcmFile(string file)
        {
            if (!DicomFile.HasValidHeader(file))
            {
                logger.Warn("{0} is not a valid DICOM file.", file);
                return;
            }

            var dcmFile = await DicomFile.OpenAsync(file);
            var dataset = dcmFile.Dataset;

            string modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty);

            if (modality != "ECG")
            {
                logger.Info("{0} is not a ECG file.", file);
                return;
            }

            if (!dataset.Contains(DicomTag.WaveformSequence))
            {
                logger.Info("Dataset not contains Waveform Sequence (5400,0100).");
                return;
            }

            GetWaveformData(dataset.GetSequence(DicomTag.WaveformSequence));

            Redraw(chartBitmap.PixelWidth, chartBitmap.PixelHeight);
        }

        /// <summary>
        /// get waveform data from dataset
        /// </summary>
        /// <param name="waveform"></param>
        private void GetWaveformData(DicomSequence waveform)
        {
            if (waveform.Items.Count == 0)
                return;

            // first dataset
            ushort channels = waveform.Items[0].GetSingleValue<ushort>(DicomTag.NumberOfWaveformChannels);
            ulong samples = waveform.Items[0].GetSingleValue<ulong>(DicomTag.NumberOfWaveformSamples);

            ushort[] temp = waveform.Items[0].GetValues<ushort>(DicomTag.WaveformData);
            short[] temp2 = new short[temp.Length];
            Buffer.BlockCopy(temp, 0, temp2, 0, temp.Length * sizeof(ushort));

            waveformData = new short[channels, samples];

            for (int i = 0; i < channels; i++)
            {
                for (ulong j = 0; j < samples; j++)
                {
                    waveformData[i, j] = temp2[(int)(j * channels) + i];
                }
            }

            hasData = true;
        }

        /// <summary>
        /// calculate draw area size
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private bool CalculateCellSize(int width, int height)
        {
            int cellSize = height / rows;

            if (cellSize > this.cellSize * 1.1 ||
                cellSize < this.cellSize * 0.9)
            {
                this.cellSize = cellSize;
                this.spacing = cellSize / ticks;
                this.columns = (int)(width / cellSize);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Redraw canvas
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void Redraw(int width, int height)
        {
            chartBitmap.Lock();

            using Bitmap bitmap = new Bitmap(
                chartBitmap.PixelWidth, chartBitmap.PixelHeight,
                chartBitmap.BackBufferStride,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb,
                chartBitmap.BackBuffer);

            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.White);

            DrawGrid(graphics, width, height);

            if (hasData)
            {
                DrawWaveform(graphics);
            }

            chartBitmap.AddDirtyRect(new Int32Rect(0, 0, chartBitmap.PixelWidth, chartBitmap.PixelHeight));
            chartBitmap.Unlock();
        }

        /// <summary>
        /// draw grid lines
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void DrawGrid(Graphics graphics, int width, int height)
        {
            // pen
            System.Drawing.Pen pen1 = new System.Drawing.Pen(System.Drawing.Brushes.Red, 1);
            System.Drawing.Pen pen2 = new System.Drawing.Pen(System.Drawing.Brushes.PaleVioletRed, 0.5f);

            // draw row lines
            int y = 0;

            for (int i = 0; i < rows; i++)
            {
                graphics.DrawLine(pen1, 0, y, width, y);

                y += spacing;

                for (int j = 1; j < ticks; j++)
                {
                    graphics.DrawLine(pen2, 0, y, width, y);

                    y += spacing;
                }
            }

            graphics.DrawLine(pen1, 0, y, width, y);

            // draw column lines
            int x = 0;

            for (int i = 0; i <= columns; i++)
            {
                if (x > width)
                    break;

                graphics.DrawLine(pen1, x, 0, x, height);

                x += spacing;

                for (int j = 1; j < ticks; j++)
                {
                    if (x > width)
                        break;

                    graphics.DrawLine(pen2, x, 0, x, height);

                    x += spacing;
                }
            }

            graphics.DrawLine(pen2, x, 0, x, height);
        }

        /// <summary>
        /// draw waveform
        /// </summary>
        private void DrawWaveform(Graphics graphics)
        {
            int channels = waveformData.GetLength(0);
            int samples = waveformData.GetLength(1);

            float scaleX = (float)cellSize * columns / samples;
            float scaleY = (float)cellSize / maxData;

            for (int i = 0; i < channels; i++)
            {
                if (channels > rows / 2)
                {
                    // maximum channels: 12
                    break;
                }

                int offsetY = spacing * ticks * (2 * i + 1);

                System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Brushes.Black, 1.5f);

                PointF[] points = new PointF[samples];

                for (int j = 0; j < samples; j++)
                {
                    points[j] = new PointF(j * scaleX, offsetY - waveformData[i, j] * scaleY);
                }

                graphics.DrawLines(pen, points);
            }
        }
    }

    internal enum Lead
    {
        Regular,
        L3_4,
        L3_4_1,
        L3_4_3,
        L6_2,
        AverageComplex
    }
}
