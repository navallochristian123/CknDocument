// Quick script to check audit log data
// Run with: dotnet script CheckAudit.csx

#r "nuget: Microsoft.Data.SqlClient, 5.1.0"

using Microsoft.Data.SqlClient;

var connStr = "Server=db40948.databaseasp.net;Database=db40948;User Id=db40948;Password=3Lr_R-6ym7B#;TrustServerCertificate=True";
var conn = new SqlConnection(connStr);
conn.Open();
Console.WriteLine("Connected!");

// Count total
var cmd1 = new SqlCommand("SELECT COUNT(*) FROM Audit_Log", conn);
Console.WriteLine($"Total Audit_Log records: {cmd1.ExecuteScalar()}");

// Show recent records
var cmd2 = new SqlCommand("SELECT TOP 20 AuditID, UserID, SuperAdminId, FirmID, Action, EntityType, ActionCategory, Description, Timestamp FROM Audit_Log ORDER BY AuditID DESC", conn);
var reader2 = cmd2.ExecuteReader();
while (reader2.Read())
{
    var uid = reader2.IsDBNull(1) ? "NULL" : reader2.GetInt32(1).ToString();
    var saId = reader2.IsDBNull(2) ? "NULL" : reader2.GetInt32(2).ToString();
    var fid = reader2.IsDBNull(3) ? "NULL" : reader2.GetInt32(3).ToString();
    var cat = reader2.IsDBNull(6) ? "NULL" : reader2.GetString(6);
    var desc = reader2.IsDBNull(7) ? "NULL" : reader2.GetString(7);
    Console.WriteLine($"ID={reader2.GetInt32(0)}, UserID={uid}, SuperAdminId={saId}, FirmID={fid}, Action={reader2.GetString(4)}, EntityType={reader2.GetString(5)}, Category={cat}, Desc={desc}, Time={reader2.GetDateTime(8)}");
}
reader2.Close();

// Count by FirmID
Console.WriteLine("\n--- Count by FirmID ---");
var cmd3 = new SqlCommand("SELECT FirmID, COUNT(*) as cnt FROM Audit_Log GROUP BY FirmID", conn);
var reader3 = cmd3.ExecuteReader();
while (reader3.Read())
{
    var fid3 = reader3.IsDBNull(0) ? "NULL" : reader3.GetInt32(0).ToString();
    Console.WriteLine($"FirmID={fid3}: {reader3.GetInt32(1)} records");
}
reader3.Close();

// Count by UserID
Console.WriteLine("\n--- Count by UserID ---");
var cmd4 = new SqlCommand("SELECT UserID, COUNT(*) as cnt FROM Audit_Log GROUP BY UserID ORDER BY cnt DESC", conn);
var reader4 = cmd4.ExecuteReader();
while (reader4.Read())
{
    var uid4 = reader4.IsDBNull(0) ? "NULL" : reader4.GetInt32(0).ToString();
    Console.WriteLine($"UserID={uid4}: {reader4.GetInt32(1)} records");
}
reader4.Close();

conn.Close();
