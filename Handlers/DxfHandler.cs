using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Diagnostics;

namespace CADTR.Handlers
{
    public class DxfHandler
    {
        private DxfFile dxfFile;
        private Rect boundingBox;
        private readonly TransformGroup transformGroup;
        private Point lastPanPosition;
        private bool isPanning;
        private const double MIN_SCALE = 0.1;
        private const double MAX_SCALE = 10.0;
        private const double ZOOM_FACTOR = 1.2;
        private const double CLICK_TOLERANCE = 5.0;

        public event Action<Polyline> PolygonSelected;

        public DxfHandler()
        {
            transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new TranslateTransform(0, 0));
        }

        public bool LoadDXF(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    dxfFile = DxfFile.Load(fs);
                }

                ComputeBoundingBox();
                ResetTransform();
                Debug.WriteLine($"✅ DXF file loaded successfully: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error loading DXF file: {ex.Message}");
                MessageBox.Show($"Failed to load DXF file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ComputeBoundingBox()
        {
            if (dxfFile == null || !dxfFile.Entities.Any())
            {
                boundingBox = new Rect(0, 0, 100, 100); // Default size if no entities
                return;
            }

            var vertices = dxfFile.Entities
                .OfType<DxfLwPolyline>()
                .SelectMany(p => p.Vertices)
                .ToList();

            if (!vertices.Any())
            {
                boundingBox = new Rect(0, 0, 100, 100);
                return;
            }

            double minX = vertices.Min(v => v.X);
            double minY = vertices.Min(v => v.Y);
            double maxX = vertices.Max(v => v.X);
            double maxY = vertices.Max(v => v.Y);

            // Add 5% padding
            double padding = Math.Max(maxX - minX, maxY - minY) * 0.05;
            boundingBox = new Rect(
                minX - padding,
                minY - padding,
                (maxX - minX) + (padding * 2),
                (maxY - minY) + (padding * 2)
            );

            Debug.WriteLine($"Bounding box computed: {boundingBox}");
        }

        public List<Polyline> ExtractPolygons()
        {
            var polygons = new List<Polyline>();
            if (dxfFile == null)
            {
                Debug.WriteLine("❌ No DXF file loaded");
                return polygons;
            }

            try
            {
                foreach (var entity in dxfFile.Entities.OfType<DxfLwPolyline>())
                {
                    var polyline = new Polyline
                    {
                        Stroke = new SolidColorBrush(Colors.LightGreen),
                        StrokeThickness = 1,
                        Fill = Brushes.Transparent
                    };

                    // Convert DXF coordinates to screen coordinates
                    foreach (var vertex in entity.Vertices)
                    {
                        polyline.Points.Add(TransformToScreen(new Point(vertex.X, vertex.Y)));
                    }

                    // Close the polygon if it's not already closed
                    if (entity.IsClosed && polyline.Points.Count > 0)
                    {
                        if (!polyline.Points[0].Equals(polyline.Points[polyline.Points.Count - 1]))
                        {
                            polyline.Points.Add(polyline.Points[0]);
                        }
                    }

                    polyline.MouseEnter += (s, e) => 
                    {
                        if (s is Polyline p)
                        {
                            p.StrokeThickness = 2;
                            p.Stroke = new SolidColorBrush(Colors.Yellow);
                        }
                    };

                    polyline.MouseLeave += (s, e) => 
                    {
                        if (s is Polyline p)
                        {
                            p.StrokeThickness = 1;
                            p.Stroke = new SolidColorBrush(Colors.LightGreen);
                        }
                    };

                    polyline.MouseLeftButtonDown += (s, e) => OnPolygonClicked(s as Polyline, e);
                    polygons.Add(polyline);
                }

                Debug.WriteLine($"✅ Extracted {polygons.Count} polygons");
                return polygons;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error extracting polygons: {ex.Message}");
                return new List<Polyline>();
            }
        }

        private Point TransformToScreen(Point dxfPoint)
        {
            // Convert DXF coordinates to screen coordinates
            double scaleX = 800 / boundingBox.Width; // Assuming 800px canvas width
            double scaleY = 600 / boundingBox.Height; // Assuming 600px canvas height
            double scale = Math.Min(scaleX, scaleY) * 0.9; // 90% to leave margin

            return new Point(
                (dxfPoint.X - boundingBox.X) * scale,
                (boundingBox.Height - (dxfPoint.Y - boundingBox.Y)) * scale
            );
        }

        private void OnPolygonClicked(Polyline polygon, MouseButtonEventArgs e)
        {
            if (polygon == null) return;

            e.Handled = true; // Prevent event bubbling
            PolygonSelected?.Invoke(polygon);
        }

        public void InitializeZoomAndPan(Canvas canvas)
        {
            canvas.RenderTransform = transformGroup;

            canvas.MouseWheel += OnMouseWheel;
            canvas.MouseDown += OnMouseDown;
            canvas.MouseMove += OnMouseMove;
            canvas.MouseUp += OnMouseUp;

            // Enable smooth scrolling
            canvas.UseLayoutRounding = true;
            RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!(sender is Canvas canvas)) return;

            Point mousePos = e.GetPosition(canvas);
            double scaleFactor = e.Delta > 0 ? ZOOM_FACTOR : 1 / ZOOM_FACTOR;

            ScaleTransform scaleTransform = transformGroup.Children[0] as ScaleTransform;
            TranslateTransform translateTransform = transformGroup.Children[1] as TranslateTransform;

            if (scaleTransform != null && translateTransform != null)
            {
                double newScale = scaleTransform.ScaleX * scaleFactor;
                if (newScale < MIN_SCALE || newScale > MAX_SCALE) return;

                Point relative = mousePos;
                translateTransform.X -= relative.X * (scaleFactor - 1) * scaleTransform.ScaleX;
                translateTransform.Y -= relative.Y * (scaleFactor - 1) * scaleTransform.ScaleY;

                scaleTransform.ScaleX = newScale;
                scaleTransform.ScaleY = newScale;
            }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                isPanning = true;
                lastPanPosition = e.GetPosition(sender as IInputElement);
                Mouse.OverrideCursor = Cursors.Hand;
                (sender as UIElement)?.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isPanning) return;

            Point currentPosition = e.GetPosition(sender as IInputElement);
            Vector delta = currentPosition - lastPanPosition;

            TranslateTransform translateTransform = transformGroup.Children[1] as TranslateTransform;
            if (translateTransform != null)
            {
                translateTransform.X += delta.X;
                translateTransform.Y += delta.Y;
            }

            lastPanPosition = currentPosition;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Released && isPanning)
            {
                isPanning = false;
                Mouse.OverrideCursor = null;
                (sender as UIElement)?.ReleaseMouseCapture();
            }
        }

        public void ResetTransform()
        {
            var scaleTransform = transformGroup.Children[0] as ScaleTransform;
            var translateTransform = transformGroup.Children[1] as TranslateTransform;

            if (scaleTransform != null)
            {
                scaleTransform.ScaleX = 1;
                scaleTransform.ScaleY = 1;
            }

            if (translateTransform != null)
            {
                translateTransform.X = 0;
                translateTransform.Y = 0;
            }
        }

        public void ZoomToExtent(Canvas canvas)
        {
            if (canvas == null || boundingBox.IsEmpty) return;

            double scaleX = canvas.ActualWidth / boundingBox.Width;
            double scaleY = canvas.ActualHeight / boundingBox.Height;
            double scale = Math.Min(scaleX, scaleY) * 0.9; // 90% to leave margin

            var scaleTransform = transformGroup.Children[0] as ScaleTransform;
            var translateTransform = transformGroup.Children[1] as TranslateTransform;

            if (scaleTransform != null && translateTransform != null)
            {
                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;

                // Center the content
                translateTransform.X = (canvas.ActualWidth - (boundingBox.Width * scale)) / 2;
                translateTransform.Y = (canvas.ActualHeight - (boundingBox.Height * scale)) / 2;
            }
        }

        public bool IsPointNearPolyline(Point point, Polyline polyline)
        {
            if (polyline == null || polyline.Points.Count < 2) return false;

            // Transform the click point to account for canvas transformations
            var transform = polyline.TransformToAncestor(polyline.Parent as Canvas);
            Point transformedPoint = transform.Transform(point);

            for (int i = 0; i < polyline.Points.Count - 1; i++)
            {
                Point start = polyline.Points[i];
                Point end = polyline.Points[i + 1];

                double distance = DistanceToLineSegment(transformedPoint, start, end);
                if (distance < CLICK_TOLERANCE)
                    return true;
            }

            return false;
        }

        private double DistanceToLineSegment(Point p, Point start, Point end)
        {
            double length = (end - start).Length;
            if (length == 0) return (p - start).Length;

            double t = Vector.Multiply(p - start, end - start) / (length * length);
            t = Math.Max(0, Math.Min(1, t));

            Point projection = start + (t * (end - start));
            return (p - projection).Length;
        }
    }
}
