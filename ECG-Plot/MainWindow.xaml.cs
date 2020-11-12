using Dicom;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ECG_Plot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Logging.ILoggerService logger = Logging.LoggerService.Instance;

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
        double cellSize;

        /// <summary>
        /// spacing between lines
        /// </summary>
        double spacing;

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
        }

        private void OnSizeChanged(object s, SizeChangedEventArgs e)
        {
            if (!CalculateCellSize(DrawCanvas.ActualWidth, DrawCanvas.ActualHeight))
            {
                // small changes, do not redraw
                return;
            }

            Redraw(DrawCanvas.ActualWidth, DrawCanvas.ActualHeight);
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
            Redraw(DrawCanvas.ActualWidth, DrawCanvas.ActualHeight);
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

            DrawWaveform();
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
        private bool CalculateCellSize(double width, double height)
        {
            double cellSize = height / rows;

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
        private void Redraw(double width, double height)
        {
            DrawCanvas.Children.Clear();

            DrawGrid(width, height);

            if (hasData)
            {
                DrawWaveform();
            }
        }

        /// <summary>
        /// draw grid lines
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void DrawGrid(double width, double height)
        {
            // draw row lines
            double y = 0;

            for (int i = 0; i < rows; i++)
            {
                DrawCanvas.Children.Add(new Line()
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 1,
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y
                });

                y += spacing;

                for (int j = 1; j < ticks; j++)
                {
                    DrawCanvas.Children.Add(new Line()
                    {
                        Stroke = Brushes.PaleVioletRed,
                        StrokeThickness = 1,
                        StrokeDashArray = { 1, 1 },
                        X1 = 0,
                        Y1 = y,
                        X2 = width,
                        Y2 = y
                    });

                    y += spacing;
                }
            }

            DrawCanvas.Children.Add(new Line()
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y
            });

            // draw column lines
            double x = 0;

            for (int i = 0; i <= columns; i++)
            {
                if (x > width)
                    break;

                DrawCanvas.Children.Add(new Line()
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 1,
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height
                });

                x += spacing;

                for (int j = 1; j < ticks; j++)
                {
                    if (x > width)
                        break;

                    DrawCanvas.Children.Add(new Line()
                    {
                        Stroke = Brushes.PaleVioletRed,
                        StrokeThickness = 1,
                        StrokeDashArray = { 1, 1 },
                        X1 = x,
                        Y1 = 0,
                        X2 = x,
                        Y2 = height
                    });

                    x += spacing;
                }
            }

            DrawCanvas.Children.Add(new Line()
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height
            });
        }

        /// <summary>
        /// draw waveform
        /// </summary>
        private void DrawWaveform()
        {
            int channels = waveformData.GetLength(0);
            int samples = waveformData.GetLength(1);

            double scaleX = cellSize * columns / samples;
            double scaleY = cellSize / maxData;

            for (int i = 0; i < channels; i++)
            {
                if (channels > rows / 2)
                {
                    // maximum channels: 12
                    break;
                }

                double offsetY = spacing * ticks * (2 * i + 1);

                Polyline polyline = new Polyline()
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                for (int j = 0; j < samples; j++)
                {
                    polyline.Points.Add(new Point(j * scaleX, offsetY - waveformData[i, j] * scaleY));
                }

                DrawCanvas.Children.Add(polyline);
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
