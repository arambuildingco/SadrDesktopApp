param([string]$Database = "SadrTest")
$cs = "Data Source=.\SQLExpress;Initial Catalog=$Database;User ID=sa;Password=123asd!@#ASD;TrustServerCertificate=True"
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
$cmd = $con.CreateCommand()
$cmd.CommandText = "SELECT c.name, t.name AS type, c.max_length, c.is_nullable, c.is_identity FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.Cashes') ORDER BY c.column_id"
$r = $cmd.ExecuteReader()
while ($r.Read()) { Write-Output ("{0} | {1}({2}) | nullable={3} | identity={4}" -f $r['name'], $r['type'], $r['max_length'], $r['is_nullable'], $r['is_identity']) }
$r.Close()
$con.Close()
Write-Output "DONE"
