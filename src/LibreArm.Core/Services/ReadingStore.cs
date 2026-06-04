namespace LibreArm.Core.Services;

using LibreArm.Core.Models;
using Microsoft.Data.Sqlite;

public sealed class ReadingStore
{
    private const int CurrentSchemaVersion = 4;
    private readonly string _databasePath;

    public ReadingStore(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = databasePath;
    }

    public async Task InitializeAsync()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        if (!await HasCurrentSchemaAsync(connection))
        {
            await DropSchemaAsync(connection);
        }

        await CreateSchemaAsync(connection);
        await SetSettingAsync(connection, "schema_version", CurrentSchemaVersion.ToString());
    }

    public async Task<IReadOnlyList<UserProfile>> LoadProfilesAsync()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, birth_date, biological_sex, photo_path, created_at, updated_at
            FROM profiles
            ORDER BY lower(display_name), id;
            """;

        var profiles = new List<UserProfile>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public async Task<UserProfile> CreateProfileAsync(string displayName, DateOnly birthDate, BiologicalSex biologicalSex)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.Now;
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO profiles (display_name, birth_date, biological_sex, created_at, updated_at)
            VALUES ($display_name, $birth_date, $biological_sex, $created_at, $updated_at)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$display_name", NormalizeProfileName(displayName));
        command.Parameters.AddWithValue("$birth_date", birthDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$biological_sex", biologicalSex.ToString());
        command.Parameters.AddWithValue("$created_at", now.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("O"));
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return new UserProfile(id, NormalizeProfileName(displayName), birthDate, biologicalSex, null, now, now);
    }

    public async Task<UserProfile> UpdateProfileAsync(UserProfile profile, string displayName, DateOnly birthDate, BiologicalSex biologicalSex)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.Now;
        var normalizedName = NormalizeProfileName(displayName);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE profiles
            SET display_name = $display_name,
                birth_date = $birth_date,
                biological_sex = $biological_sex,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$display_name", normalizedName);
        command.Parameters.AddWithValue("$birth_date", birthDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$biological_sex", biologicalSex.ToString());
        command.Parameters.AddWithValue("$updated_at", now.ToString("O"));
        command.Parameters.AddWithValue("$id", profile.Id);
        await command.ExecuteNonQueryAsync();
        return profile with { DisplayName = normalizedName, BirthDate = birthDate, BiologicalSex = biologicalSex, UpdatedAt = now };
    }

    public async Task<UserProfile> UpdateProfilePhotoAsync(UserProfile profile, string? photoPath)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.Now;
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE profiles
            SET photo_path = $photo_path,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$photo_path", photoPath is null ? DBNull.Value : photoPath);
        command.Parameters.AddWithValue("$updated_at", now.ToString("O"));
        command.Parameters.AddWithValue("$id", profile.Id);
        await command.ExecuteNonQueryAsync();
        return profile with { PhotoPath = photoPath, UpdatedAt = now };
    }

    public async Task DeleteProfileAsync(long profileId)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", profileId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var values = new Dictionary<string, string?>();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        values.TryGetValue("remembered_device_name", out var name);
        values.TryGetValue("remembered_device_address", out var address);
        return new AppSettings(name, address);
    }

    public async Task SaveRememberedDeviceAsync(string deviceName, string bluetoothAddress)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        await SetSettingAsync(connection, "remembered_device_name", deviceName);
        await SetSettingAsync(connection, "remembered_device_address", bluetoothAddress);
    }

    public async Task<IReadOnlyList<MeasurementSessionItem>> LoadRecentSessionsAsync(long profileId, int limit = 100)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, profile_id, started_at, completed_at,
                   average_systolic, average_diastolic, average_mean_arterial_pressure, average_pulse_rate,
                   unit, measurement_status, device_name, bluetooth_address
            FROM measurement_sessions
            WHERE profile_id = $profile_id
            ORDER BY completed_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<SessionRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadSessionRow(reader));
        }

        var sessions = new List<MeasurementSessionItem>();
        foreach (var row in rows)
        {
            var session = await ReadSessionAsync(connection, row);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    public async Task<IReadOnlyList<WeeklyBloodPressureSummary>> LoadWeeklySummariesAsync(long profileId, int weeks = 12)
    {
        var currentWeekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.Today));
        var requestedWeeks = Enumerable.Range(0, weeks)
            .Select(offset => currentWeekStart.AddDays(-7 * (weeks - 1 - offset)))
            .ToList();

        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT completed_at, average_systolic, average_diastolic, average_mean_arterial_pressure, average_pulse_rate
            FROM measurement_sessions
            WHERE profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);

        var groups = requestedWeeks.ToDictionary(week => week, _ => new List<WeeklyRow>());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var completedAt = DateTimeOffset.Parse(reader.GetString(0)).LocalDateTime;
            var weekStart = GetWeekStart(DateOnly.FromDateTime(completedAt));
            if (!groups.TryGetValue(weekStart, out var rows))
            {
                continue;
            }

            rows.Add(new WeeklyRow(
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        }

        return requestedWeeks
            .Select(week =>
            {
                var rows = groups[week];
                return new WeeklyBloodPressureSummary(
                    week,
                    week.AddDays(6),
                    rows.Count,
                    rows.Count == 0 ? null : rows.Average(r => r.Systolic),
                    rows.Count == 0 ? null : rows.Average(r => r.Diastolic),
                    rows.Count == 0 ? null : rows.Average(r => r.MeanArterialPressure),
                    AverageNullable(rows.Select(r => r.PulseRate)));
            })
            .ToList();
    }

    public async Task<MeasurementSessionItem> SaveSessionAsync(
        long profileId,
        BloodPressureReading firstReading,
        BloodPressureReading secondReading,
        BloodPressureReading averageReading,
        string deviceName,
        string bluetoothAddress)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var startedAt = firstReading.ReceivedAt;
        var completedAt = DateTimeOffset.Now;

        var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = (SqliteTransaction)transaction;
        sessionCommand.CommandText = """
            INSERT INTO measurement_sessions (
                profile_id, started_at, completed_at,
                average_systolic, average_diastolic, average_mean_arterial_pressure,
                average_pulse_rate, unit, measurement_status, device_name, bluetooth_address
            )
            VALUES (
                $profile_id, $started_at, $completed_at,
                $average_systolic, $average_diastolic, $average_mean_arterial_pressure,
                $average_pulse_rate, $unit, $measurement_status, $device_name, $bluetooth_address
            )
            RETURNING id;
            """;
        AddSessionParameters(sessionCommand, profileId, startedAt, completedAt, averageReading, deviceName, bluetoothAddress);
        var sessionId = (long)(await sessionCommand.ExecuteScalarAsync() ?? 0L);

        await SaveReadingAsync(connection, (SqliteTransaction)transaction, profileId, sessionId, 1, firstReading, deviceName, bluetoothAddress);
        await SaveReadingAsync(connection, (SqliteTransaction)transaction, profileId, sessionId, 2, secondReading, deviceName, bluetoothAddress);
        await transaction.CommitAsync();

        return new MeasurementSessionItem(sessionId, profileId, startedAt, completedAt, averageReading, firstReading, secondReading, deviceName, bluetoothAddress);
    }

    public async Task ClearProfileDataAsync(long profileId)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM measurement_sessions WHERE profile_id = $profile_id;
            DELETE FROM readings WHERE profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        await command.ExecuteNonQueryAsync();
    }

    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath};Pooling=False");
    }

    private static async Task<bool> HasCurrentSchemaAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "app_settings"))
        {
            return false;
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = 'schema_version';";
        var value = await command.ExecuteScalarAsync();
        return value?.ToString() == CurrentSchemaVersion.ToString();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }

    private static async Task DropSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS readings;
            DROP TABLE IF EXISTS measurement_sessions;
            DROP TABLE IF EXISTS profiles;
            DROP TABLE IF EXISTS app_settings;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS profiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                display_name TEXT NOT NULL,
                birth_date TEXT NOT NULL,
                biological_sex TEXT NOT NULL,
                photo_path TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS measurement_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                average_systolic REAL NOT NULL,
                average_diastolic REAL NOT NULL,
                average_mean_arterial_pressure REAL NOT NULL,
                average_pulse_rate REAL NULL,
                unit TEXT NOT NULL,
                measurement_status INTEGER NULL,
                device_name TEXT NOT NULL,
                bluetooth_address TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES profiles(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS readings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER NOT NULL,
                session_id INTEGER NULL,
                reading_number INTEGER NULL,
                received_at TEXT NOT NULL,
                measured_at TEXT NULL,
                systolic REAL NOT NULL,
                diastolic REAL NOT NULL,
                mean_arterial_pressure REAL NOT NULL,
                unit TEXT NOT NULL,
                pulse_rate REAL NULL,
                user_id INTEGER NULL,
                measurement_status INTEGER NULL,
                raw_payload TEXT NOT NULL,
                device_name TEXT NOT NULL,
                bluetooth_address TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES profiles(id) ON DELETE CASCADE,
                FOREIGN KEY (session_id) REFERENCES measurement_sessions(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetSettingAsync(SqliteConnection connection, string key, string? value)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value is null ? DBNull.Value : value);
        await command.ExecuteNonQueryAsync();
    }

    private static UserProfile ReadProfile(SqliteDataReader reader)
    {
        return new UserProfile(
            reader.GetInt64(0),
            reader.GetString(1),
            DateOnly.Parse(reader.GetString(2)),
            ParseBiologicalSex(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static SessionRow ReadSessionRow(SqliteDataReader reader)
    {
        return new SessionRow(
            reader.GetInt64(0),
            reader.GetInt64(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.GetDouble(4),
            reader.GetDouble(5),
            reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            ParseUnit(reader.GetString(8)),
            reader.IsDBNull(9) ? null : (ushort)reader.GetInt32(9),
            reader.GetString(10),
            reader.GetString(11));
    }

    private static async Task<MeasurementSessionItem?> ReadSessionAsync(SqliteConnection connection, SessionRow row)
    {
        var average = new BloodPressureReading(
            row.AverageSystolic,
            row.AverageDiastolic,
            row.AverageMeanArterialPressure,
            row.Unit,
            row.AveragePulseRate,
            row.CompletedAt.LocalDateTime,
            null,
            row.MeasurementStatus,
            row.CompletedAt,
            []);

        var readings = await LoadSessionReadingsAsync(connection, row.Id);
        if (readings.Count < 2)
        {
            return null;
        }

        return new MeasurementSessionItem(row.Id, row.ProfileId, row.StartedAt, row.CompletedAt, average, readings[0], readings[1], row.DeviceName, row.BluetoothAddress);
    }

    private static async Task<IReadOnlyList<BloodPressureReading>> LoadSessionReadingsAsync(SqliteConnection connection, long sessionId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT received_at, measured_at, systolic, diastolic, mean_arterial_pressure, unit,
                   pulse_rate, user_id, measurement_status, raw_payload
            FROM readings
            WHERE session_id = $session_id
            ORDER BY reading_number;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);

        var readings = new List<BloodPressureReading>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            readings.Add(ReadReading(reader));
        }

        return readings;
    }

    private static BloodPressureReading ReadReading(SqliteDataReader reader)
    {
        var receivedAt = DateTimeOffset.Parse(reader.GetString(0));
        DateTime? measuredAt = reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1));
        return new BloodPressureReading(
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            ParseUnit(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            measuredAt,
            reader.IsDBNull(7) ? null : (byte)reader.GetInt32(7),
            reader.IsDBNull(8) ? null : (ushort)reader.GetInt32(8),
            receivedAt,
            Convert.FromHexString(reader.GetString(9)));
    }

    private static async Task SaveReadingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long profileId,
        long sessionId,
        int readingNumber,
        BloodPressureReading reading,
        string deviceName,
        string bluetoothAddress)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO readings (
                profile_id, session_id, reading_number, received_at, measured_at,
                systolic, diastolic, mean_arterial_pressure, unit, pulse_rate,
                user_id, measurement_status, raw_payload, device_name, bluetooth_address
            )
            VALUES (
                $profile_id, $session_id, $reading_number, $received_at, $measured_at,
                $systolic, $diastolic, $mean_arterial_pressure, $unit, $pulse_rate,
                $user_id, $measurement_status, $raw_payload, $device_name, $bluetooth_address
            );
            """;
        AddReadingParameters(command, profileId, sessionId, readingNumber, reading, deviceName, bluetoothAddress);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddSessionParameters(
        SqliteCommand command,
        long profileId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        BloodPressureReading averageReading,
        string deviceName,
        string bluetoothAddress)
    {
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("O"));
        command.Parameters.AddWithValue("$completed_at", completedAt.ToString("O"));
        command.Parameters.AddWithValue("$average_systolic", averageReading.Systolic);
        command.Parameters.AddWithValue("$average_diastolic", averageReading.Diastolic);
        command.Parameters.AddWithValue("$average_mean_arterial_pressure", averageReading.MeanArterialPressure);
        command.Parameters.AddWithValue("$average_pulse_rate", averageReading.PulseRate is null ? DBNull.Value : averageReading.PulseRate.Value);
        command.Parameters.AddWithValue("$unit", averageReading.Unit.ToString());
        command.Parameters.AddWithValue("$measurement_status", averageReading.MeasurementStatus is null ? DBNull.Value : averageReading.MeasurementStatus.Value);
        command.Parameters.AddWithValue("$device_name", deviceName);
        command.Parameters.AddWithValue("$bluetooth_address", bluetoothAddress);
    }

    private static void AddReadingParameters(
        SqliteCommand command,
        long profileId,
        long sessionId,
        int readingNumber,
        BloodPressureReading reading,
        string deviceName,
        string bluetoothAddress)
    {
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$session_id", sessionId);
        command.Parameters.AddWithValue("$reading_number", readingNumber);
        command.Parameters.AddWithValue("$received_at", reading.ReceivedAt.ToString("O"));
        command.Parameters.AddWithValue("$measured_at", reading.Timestamp is null ? DBNull.Value : reading.Timestamp.Value.ToString("O"));
        command.Parameters.AddWithValue("$systolic", reading.Systolic);
        command.Parameters.AddWithValue("$diastolic", reading.Diastolic);
        command.Parameters.AddWithValue("$mean_arterial_pressure", reading.MeanArterialPressure);
        command.Parameters.AddWithValue("$unit", reading.Unit.ToString());
        command.Parameters.AddWithValue("$pulse_rate", reading.PulseRate is null ? DBNull.Value : reading.PulseRate.Value);
        command.Parameters.AddWithValue("$user_id", reading.UserId is null ? DBNull.Value : reading.UserId.Value);
        command.Parameters.AddWithValue("$measurement_status", reading.MeasurementStatus is null ? DBNull.Value : reading.MeasurementStatus.Value);
        command.Parameters.AddWithValue("$raw_payload", Convert.ToHexString(reading.RawPayload));
        command.Parameters.AddWithValue("$device_name", deviceName);
        command.Parameters.AddWithValue("$bluetooth_address", bluetoothAddress);
    }

    private static BloodPressureUnit ParseUnit(string value)
    {
        return Enum.TryParse<BloodPressureUnit>(value, out var parsed)
            ? parsed
            : BloodPressureUnit.MillimetersOfMercury;
    }

    private static BiologicalSex ParseBiologicalSex(string value)
    {
        return Enum.TryParse<BiologicalSex>(value, out var parsed) ? parsed : BiologicalSex.Unspecified;
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var concrete = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return concrete.Count == 0 ? null : concrete.Average();
    }

    private static string NormalizeProfileName(string displayName)
    {
        var normalized = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Profile name is required.", nameof(displayName));
        }

        return normalized;
    }

    private sealed record SessionRow(
        long Id,
        long ProfileId,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        double AverageSystolic,
        double AverageDiastolic,
        double AverageMeanArterialPressure,
        double? AveragePulseRate,
        BloodPressureUnit Unit,
        ushort? MeasurementStatus,
        string DeviceName,
        string BluetoothAddress);

    private sealed record WeeklyRow(
        double Systolic,
        double Diastolic,
        double MeanArterialPressure,
        double? PulseRate);
}
