using CADTR.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Diagnostics;
using System.Data;
using System.Windows.Threading;

namespace CADTR.Views
{
    public partial class MainWindow : Window
    {
        private readonly DxfHandler dxfHandler;
        private readonly DatabaseHandler dbHandler;
        private readonly Dictionary<Polyline, bool> savedPolygons;
        private readonly string dbPath;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                // Initialize handlers and collections
                dbPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "CADTR",
                    "ProjectData.db"
                );

                // Ensure directory exists
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath));

                dxfHandler = new DxfHandler();
                dbHandler = new DatabaseHandler(dbPath);
                savedPolygons = new Dictionary<Polyline, bool>();

                // Initialize UI
                InitializeUI();

                // Subscribe to events
                dxfHandler.PolygonSelected += OnPolygonSelected;
                Loaded += MainWindow_Loaded;

                StatusTextBlock.Text = "Ready";
                Debug.WriteLine("✅ Application initialized successfully");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize application: {ex.Message}", 
                    "Initialization Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                Debug.WriteLine($"❌ Initialization error: {ex}");
                Close();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize canvas transform
                dxfHandler.InitializeZoomAndPan(drawingCanvas);
                
                // Test database connection
                dbHandler.TestDatabaseConnection();
                
                // Load saved data
                RefreshSavedParcels();
                UpdateLayerTree();
            }
            catch (Exception ex)
            {
                HandleError("Failed to complete initial setup", ex);
            }
        }

        private void InitializeUI()
        {
            // Configure DataGrid columns
            dataGridParcels.AutoGenerateColumns = false;
            dataGridParcels.Columns.Add(new DataGridTextColumn 
            { 
                Header = "ID", 
                Binding = new System.Windows.Data.Binding("PolygonID") 
            });
            dataGridParcels.Columns.Add(new DataGridTextColumn 
            { 
                Header = "Name", 
                Binding = new System.Windows.Data.Binding("PolygonName") 
            });
            dataGridParcels.Columns.Add(new DataGridTextColumn 
            { 
                Header = "Vertices", 
                Binding = new System.Windows.Data.Binding("VertexCount") 
            });
            dataGridParcels.Columns.Add(new DataGridTextColumn 
            { 
                Header = "Created", 
                Binding = new System.Windows.Data.Binding("CreatedAt") 
            });
        }

        private void btnLoadDXF_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "DXF files (*.dxf)|*.dxf",
                Title = "Open DXF File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    StatusTextBlock.Text = "Loading DXF...";
                    drawingCanvas.Children.Clear();
                    savedPolygons.Clear();

                    if (dxfHandler.LoadDXF(openFileDialog.FileName))
                    {
                        LoadPolygonsToCanvas();
                        dxfHandler.ZoomToExtent(drawingCanvas);
                        StatusTextBlock.Text = "DXF Loaded Successfully";
                    }
                }
                catch (Exception ex)
                {
                    HandleError("Failed to load DXF file", ex);
                }
            }
        }

        private void LoadPolygonsToCanvas()
        {
            var polygons = dxfHandler.ExtractPolygons();
            if (!polygons.Any())
            {
                MessageBox.Show("No polygons found in the DXF file.", 
                    "DXF Import", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            foreach (var polygon in polygons)
            {
                drawingCanvas.Children.Add(polygon);
                savedPolygons[polygon] = false;
            }

            StatusTextBlock.Text = $"Loaded {polygons.Count} polygons";
        }

        private void OnPolygonSelected(Polyline polygon)
        {
            if (polygon == null) return;

            try
            {
                List<Point> vertices = polygon.Points.ToList();
                int existingPolygonId = dbHandler.GetPolygonIdByVertices(vertices);

                if (existingPolygonId > 0)
                {
                    HandleExistingPolygon(polygon, existingPolygonId);
                }
                else
                {
                    HandleNewPolygon(polygon);
                }
            }
            catch (Exception ex)
            {
                HandleError("Error processing polygon selection", ex);
            }
        }

        private void HandleExistingPolygon(Polyline polygon, int polygonId)
        {
            var result = MessageBox.Show(
                "This polygon is already saved. Would you like to rename it?",
                "Polygon Exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new InputDialog("Enter New Polygon Name:");
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        dbHandler.RenamePolygon(polygonId, dialog.Input);
                        RefreshSavedParcels();
                        UpdateLayerTree();
                        StatusTextBlock.Text = $"Polygon renamed to '{dialog.Input}'";
                    }
                    catch (Exception ex)
                    {
                        HandleError("Failed to rename polygon", ex);
                    }
                }
            }
        }

        private void HandleNewPolygon(Polyline polygon)
        {
            var result = MessageBox.Show(
                "Would you like to save this polygon?",
                "Save Polygon",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new InputDialog("Enter Polygon Name:");
                if (dialog.ShowDialog() == true)
                {
                    SavePolygon(polygon, dialog.Input);
                }
            }
        }

        private void SavePolygon(Polyline polygon, string name)
        {
            try
            {
                var vertices = polygon.Points.ToList();
                int polygonId = dbHandler.InsertPolygon(name, "Cadastral Parcel", vertices);

                if (polygonId > 0)
                {
                    savedPolygons[polygon] = true;
                    polygon.Stroke = new SolidColorBrush(Colors.LightBlue);
                    
                    RefreshSavedParcels();
                    UpdateLayerTree();
                    
                    StatusTextBlock.Text = $"Polygon '{name}' saved successfully";
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to save polygon", ex);
            }
        }

        private void RefreshSavedParcels()
        {
            try
            {
                dataGridParcels.ItemsSource = dbHandler.GetSavedPolygons().DefaultView;
            }
            catch (Exception ex)
            {
                HandleError("Failed to refresh saved parcels", ex);
            }
        }

        private void UpdateLayerTree()
        {
            try
            {
                treePolygons.Items.Clear();
                var dt = dbHandler.GetSavedPolygons();
                
                foreach (DataRow row in dt.Rows)
                {
                    var item = new TreeViewItem
                    {
                        Header = row["PolygonName"].ToString(),
                        Tag = row["PolygonID"]
                    };
                    treePolygons.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to update layer tree", ex);
            }
        }

        private void btnZoomToExtent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                dxfHandler.ZoomToExtent(drawingCanvas);
                StatusTextBlock.Text = "Zoomed to extent";
            }
            catch (Exception ex)
            {
                HandleError("Failed to zoom to extent", ex);
            }
        }

        #region Canvas Mouse Event Handlers

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Handled by DxfHandler
            e.Handled = true;
        }

        private void Canvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Handled by DxfHandler
            drawingCanvas.Focus();
            e.Handled = true;
        }

        private void Canvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Handled by DxfHandler
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Handled by DxfHandler
            e.Handled = true;
        }

        #endregion

        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"❌ Error: {message}\n{ex}");
            MessageBox.Show(
                $"{message}\n\nDetails: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            StatusTextBlock.Text = "Error occurred";
        }
    }
}
