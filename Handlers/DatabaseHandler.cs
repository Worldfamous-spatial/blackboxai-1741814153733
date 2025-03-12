using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CADTR.Handlers
{
    public class DatabaseHandler
    {
        private readonly string connectionString;
        private static readonly object _lock = new object(); // For thread safety

        public DatabaseHandler(string dbPath)
        {
            connectionString = $"Data Source={dbPath};Version=3;Foreign Keys=True;"; // Enable foreign keys
            if (!File.Exists(dbPath))
            {
                CreateDatabase(dbPath);
            }
        }

        private void CreateDatabase(string dbPath)
        {
            try
            {
                SQLiteConnection.CreateFile(dbPath);

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    // Create tables with proper constraints and indices
                    string createCoordinates = @"
                        CREATE TABLE IF NOT EXISTS Coordinates (
                            ID INTEGER PRIMARY KEY AUTOINCREMENT,
                            PointName TEXT NOT NULL UNIQUE,
                            X REAL NOT NULL,
                            Y REAL NOT NULL,
                            CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(X, Y) -- Prevent duplicate coordinates
                        );
                        CREATE INDEX IF NOT EXISTS idx_coordinates_xy ON Coordinates(X, Y);";

                    string createPolygons = @"
                        CREATE TABLE IF NOT EXISTS Polygons (
                            PolygonID INTEGER PRIMARY KEY AUTOINCREMENT,
                            PolygonName TEXT NOT NULL UNIQUE,
                            Description TEXT,
                            CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            LastModified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                        );";

                    string createPolygonVertices = @"
                        CREATE TABLE IF NOT EXISTS PolygonVertices (
                            PolygonID INTEGER NOT NULL,
                            PointID INTEGER NOT NULL,
                            VertexOrder INTEGER NOT NULL,
                            FOREIGN KEY(PolygonID) REFERENCES Polygons(PolygonID) ON DELETE CASCADE,
                            FOREIGN KEY(PointID) REFERENCES Coordinates(ID) ON DELETE CASCADE,
                            PRIMARY KEY(PolygonID, PointID),
                            UNIQUE(PolygonID, VertexOrder)
                        );";

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            ExecuteNonQuery(conn, createCoordinates);
                            ExecuteNonQuery(conn, createPolygons);
                            ExecuteNonQuery(conn, createPolygonVertices);
                            transaction.Commit();
                            Debug.WriteLine("✅ Database and tables created successfully.");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw new Exception($"Failed to create database tables: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error creating database: {ex.Message}");
                throw; // Rethrow to notify the caller
            }
        }

        private void ExecuteNonQuery(SQLiteConnection conn, string sql, Dictionary<string, object> parameters = null)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }
                cmd.ExecuteNonQuery();
            }
        }

        public int InsertPolygon(string polygonName, string description, List<Point> vertices)
        {
            if (string.IsNullOrEmpty(polygonName))
                throw new ArgumentException("Polygon name cannot be empty");

            if (vertices == null || vertices.Count < 3)
                throw new ArgumentException("A polygon must have at least 3 vertices");

            lock (_lock) // Ensure thread safety
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // Insert polygon
                            string insertPolygonSql = @"
                                INSERT INTO Polygons (PolygonName, Description) 
                                VALUES (@name, @desc);
                                SELECT last_insert_rowid();";

                            int polygonId;
                            using (var cmd = new SQLiteCommand(insertPolygonSql, conn))
                            {
                                cmd.Parameters.AddWithValue("@name", polygonName);
                                cmd.Parameters.AddWithValue("@desc", description ?? (object)DBNull.Value);
                                polygonId = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // Insert vertices
                            for (int i = 0; i < vertices.Count; i++)
                            {
                                // First try to find existing point
                                int pointId = FindOrInsertPoint(conn, vertices[i]);

                                // Link point to polygon
                                string insertVertexSql = @"
                                    INSERT INTO PolygonVertices (PolygonID, PointID, VertexOrder) 
                                    VALUES (@polygonId, @pointId, @order)";

                                using (var cmd = new SQLiteCommand(insertVertexSql, conn))
                                {
                                    cmd.Parameters.AddWithValue("@polygonId", polygonId);
                                    cmd.Parameters.AddWithValue("@pointId", pointId);
                                    cmd.Parameters.AddWithValue("@order", i);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                            Debug.WriteLine($"✅ Polygon '{polygonName}' saved successfully with {vertices.Count} vertices.");
                            return polygonId;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Debug.WriteLine($"❌ Failed to save polygon: {ex.Message}");
                            throw;
                        }
                    }
                }
            }
        }

        private int FindOrInsertPoint(SQLiteConnection conn, Point point)
        {
            // Try to find existing point with same coordinates (within tolerance)
            const double tolerance = 0.0001;
            string findPointSql = @"
                SELECT ID FROM Coordinates 
                WHERE ABS(X - @x) < @tolerance 
                AND ABS(Y - @y) < @tolerance 
                LIMIT 1";

            using (var cmd = new SQLiteCommand(findPointSql, conn))
            {
                cmd.Parameters.AddWithValue("@x", point.X);
                cmd.Parameters.AddWithValue("@y", point.Y);
                cmd.Parameters.AddWithValue("@tolerance", tolerance);
                var result = cmd.ExecuteScalar();
                
                if (result != null)
                    return Convert.ToInt32(result);
            }

            // Point not found, insert new one
            return InsertPoint(conn, point.X, point.Y);
        }

        private int InsertPoint(SQLiteConnection conn, double x, double y)
        {
            string nextPointNameSql = @"
                SELECT COALESCE(MAX(CAST(SUBSTR(PointName, 4) AS INTEGER)), 0) + 1 
                FROM Coordinates 
                WHERE PointName LIKE 'PT_%'";

            int nextNumber;
            using (var cmd = new SQLiteCommand(nextPointNameSql, conn))
            {
                nextNumber = Convert.ToInt32(cmd.ExecuteScalar());
            }

            string pointName = $"PT_{nextNumber:D4}"; // Use 4 digits padding

            string insertPointSql = @"
                INSERT INTO Coordinates (PointName, X, Y) 
                VALUES (@name, @x, @y);
                SELECT last_insert_rowid();";

            using (var cmd = new SQLiteCommand(insertPointSql, conn))
            {
                cmd.Parameters.AddWithValue("@name", pointName);
                cmd.Parameters.AddWithValue("@x", x);
                cmd.Parameters.AddWithValue("@y", y);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void RenamePolygon(int polygonId, string newName)
        {
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentException("New name cannot be empty");

            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                        UPDATE Polygons 
                        SET PolygonName = @name, LastModified = CURRENT_TIMESTAMP 
                        WHERE PolygonID = @id";

                    ExecuteNonQuery(conn, sql, new Dictionary<string, object>
                    {
                        ["@name"] = newName,
                        ["@id"] = polygonId
                    });
                }
                Debug.WriteLine($"✅ Polygon {polygonId} renamed to '{newName}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to rename polygon: {ex.Message}");
                throw;
            }
        }

        public List<Point> GetPolygonVertices(int polygonId)
        {
            var vertices = new List<Point>();
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                        SELECT c.X, c.Y 
                        FROM Coordinates c
                        INNER JOIN PolygonVertices pv ON c.ID = pv.PointID
                        WHERE pv.PolygonID = @id
                        ORDER BY pv.VertexOrder";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", polygonId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                vertices.Add(new Point(
                                    reader.GetDouble(0),
                                    reader.GetDouble(1)
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to get polygon vertices: {ex.Message}");
                throw;
            }
            return vertices;
        }

        public DataTable GetSavedPolygons()
        {
            var dt = new DataTable();
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                        SELECT p.PolygonID, p.PolygonName, p.Description,
                               p.CreatedAt, p.LastModified,
                               COUNT(pv.PointID) as VertexCount
                        FROM Polygons p
                        LEFT JOIN PolygonVertices pv ON p.PolygonID = pv.PolygonID
                        GROUP BY p.PolygonID
                        ORDER BY p.CreatedAt DESC";

                    using (var adapter = new SQLiteDataAdapter(sql, conn))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to get saved polygons: {ex.Message}");
                throw;
            }
            return dt;
        }

        public int GetPolygonIdByVertices(List<Point> vertices, double tolerance = 0.0001)
        {
            if (vertices == null || vertices.Count < 3)
                return -1;

            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    
                    // Get all polygons with the same number of vertices
                    string sql = @"
                        SELECT PolygonID 
                        FROM PolygonVertices 
                        GROUP BY PolygonID 
                        HAVING COUNT(*) = @vertexCount";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@vertexCount", vertices.Count);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int polygonId = reader.GetInt32(0);
                                var existingVertices = GetPolygonVertices(polygonId);
                                
                                // Check if vertices match (allowing for rotation)
                                if (ArePolygonsEqual(vertices, existingVertices, tolerance))
                                    return polygonId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to find polygon by vertices: {ex.Message}");
            }
            return -1;
        }

        private bool ArePolygonsEqual(List<Point> poly1, List<Point> poly2, double tolerance)
        {
            if (poly1.Count != poly2.Count) return false;

            // Try all possible rotations
            for (int start = 0; start < poly1.Count; start++)
            {
                bool match = true;
                for (int i = 0; i < poly1.Count; i++)
                {
                    int j = (start + i) % poly1.Count;
                    if (!ArePointsEqual(poly1[i], poly2[j], tolerance))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }

        private bool ArePointsEqual(Point p1, Point p2, double tolerance)
        {
            return Math.Abs(p1.X - p2.X) < tolerance && 
                   Math.Abs(p1.Y - p2.Y) < tolerance;
        }

        public void TestDatabaseConnection()
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    Debug.WriteLine("✅ Database connection test successful!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Database connection test failed: {ex.Message}");
                throw;
            }
        }
    }
}
