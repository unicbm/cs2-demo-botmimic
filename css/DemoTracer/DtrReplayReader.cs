using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DemoTracer;

internal static class DtrReplayReader
{
    private const byte RecCodecBrotli = 1;
    private const int TickMetadataByteSize = 8;
    private const int ProjectileEventByteSize = 48;

    private static readonly byte[] RecMagic =
    [
        (byte)'C', (byte)'S', (byte)'D', (byte)'T',
        (byte)'R', (byte)'R', (byte)'E', (byte)'C'
    ];

    private static readonly JsonSerializerOptions HifiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DtrReplayFile Read(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".dtr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("expected .dtr replay file");

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(RecMagic.Length);
        if (!magic.SequenceEqual(RecMagic))
            throw new InvalidDataException("bad .dtr magic");

        var version = reader.ReadUInt32();
        if (version is < BotControllerNative.MinRecFormatVersion or > BotControllerNative.RecFormatVersion)
            throw new InvalidDataException(
                $"unsupported .dtr version {version}; expected {BotControllerNative.MinRecFormatVersion}..{BotControllerNative.RecFormatVersion}");

        var tickRate = reader.ReadSingle();
        _ = reader.ReadUInt32(); // round
        _ = reader.ReadByte();   // side
        _ = reader.ReadUInt32(); // flags
        _ = reader.ReadUInt64(); // steam_id
        var tickCount = CheckedCount(reader.ReadUInt32(), "tick_count");
        var subtickCount = CheckedCount(reader.ReadUInt32(), "subtick_count");
        var projectileCount = version >= 4
            ? CheckedCount(reader.ReadUInt32(), "projectile_count")
            : 0;
        var playStartTickIndex = version >= 5
            ? CheckedCount(reader.ReadUInt32(), "play_start_tick_index")
            : 0;
        var metadataJsonLength = version >= 6
            ? CheckedCount(reader.ReadUInt32(), "metadata_json_len")
            : 0;
        ValidatePlayStartTickIndex(tickCount, playStartTickIndex);
        _ = ReadRecString(reader); // map
        _ = ReadRecString(reader); // player name

        var codec = reader.ReadByte();
        if (codec != RecCodecBrotli)
            throw new InvalidDataException($"unsupported .dtr codec {codec}");

        var bodyUncompressedLength = CheckedLength(reader.ReadUInt64(), "body_uncompressed_len");
        var bodyCompressedLength = CheckedLength(reader.ReadUInt64(), "body_compressed_len");
        var expectedBodyLength = ExpectedBodyLength(tickCount, subtickCount, projectileCount, metadataJsonLength);
        if (bodyUncompressedLength != expectedBodyLength)
            throw new InvalidDataException($"body length {bodyUncompressedLength} != expected {expectedBodyLength}");

        var compressed = reader.ReadBytes(bodyCompressedLength);
        if (compressed.Length != bodyCompressedLength)
            throw new EndOfStreamException("truncated compressed .dtr body");

        var body = DecompressBrotli(compressed, bodyUncompressedLength);
        using var bodyStream = new MemoryStream(body, writable: false);
        using var bodyReader = new BinaryReader(bodyStream);

        var snapshotCount = tickCount == 0 ? 0 : tickCount + 1;
        var snapshots = new NativeMovementSnapshot[snapshotCount];
        for (var i = 0; i < snapshotCount; i++)
            snapshots[i] = ReadCurrentSnapshot(bodyReader);

        var ticks = new NativeReplayTick[tickCount];
        long expectedSubticks = 0;
        for (var i = 0; i < tickCount; i++)
        {
            ticks[i] = new NativeReplayTick
            {
                Pre = snapshots[i],
                Post = snapshots[i + 1],
                WeaponDefIndex = bodyReader.ReadInt32(),
                NumSubtick = bodyReader.ReadUInt32()
            };
            expectedSubticks += ticks[i].NumSubtick;
        }

        if (expectedSubticks != subtickCount)
            throw new InvalidDataException($"tick subtick sum {expectedSubticks} != header subtick count {subtickCount}");

        var projectiles = new ReplayProjectileEvent[projectileCount];
        for (var i = 0; i < projectileCount; i++)
            projectiles[i] = ReadProjectileEvent(bodyReader);

        var highFidelity = ReplayHighFidelityMetadata.Empty;
        if (metadataJsonLength > 0)
        {
            var metadataJson = bodyReader.ReadBytes(metadataJsonLength);
            if (metadataJson.Length != metadataJsonLength)
                throw new EndOfStreamException("truncated high_fidelity metadata in .dtr");
            highFidelity = ReadHighFidelityMetadata(metadataJson);
        }

        var subticks = new NativeSubtickMove[subtickCount];
        for (var i = 0; i < subtickCount; i++)
        {
            subticks[i] = new NativeSubtickMove
            {
                When = bodyReader.ReadSingle(),
                Button = bodyReader.ReadUInt32(),
                Pressed = bodyReader.ReadSingle(),
                AnalogForward = bodyReader.ReadSingle(),
                AnalogLeft = bodyReader.ReadSingle(),
                PitchDelta = bodyReader.ReadSingle(),
                YawDelta = bodyReader.ReadSingle()
            };
        }

        if (bodyStream.Position != bodyStream.Length)
            throw new InvalidDataException("trailing bytes in .dtr body");

        return new DtrReplayFile(ticks, projectiles, highFidelity, subticks, tickRate, (uint)playStartTickIndex);
    }

    private static void ValidatePlayStartTickIndex(int tickCount, int playStartTickIndex)
    {
        if (tickCount == 0)
        {
            if (playStartTickIndex == 0)
                return;
            throw new InvalidDataException(
                $"play_start_tick_index {playStartTickIndex} requires at least one tick");
        }
        if (playStartTickIndex >= tickCount)
            throw new InvalidDataException(
                $"play_start_tick_index {playStartTickIndex} out of range for {tickCount} ticks");
    }

    private static int CheckedCount(uint value, string fieldName)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"{fieldName} too large: {value}");
        return (int)value;
    }

    private static int CheckedLength(ulong value, string fieldName)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"{fieldName} too large: {value}");
        return (int)value;
    }

    private static int ExpectedBodyLength(int tickCount, int subtickCount, int projectileCount, int metadataJsonLength)
    {
        var snapshotCount = tickCount == 0 ? 0 : checked(tickCount + 1);
        return checked(
            snapshotCount * BotControllerNative.MovementSnapshotByteSize +
            tickCount * TickMetadataByteSize +
            projectileCount * ProjectileEventByteSize +
            metadataJsonLength +
            subtickCount * BotControllerNative.SubtickMoveByteSize);
    }

    private static byte[] DecompressBrotli(byte[] compressed, int expectedLength)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedLength);
        brotli.CopyTo(output);
        if (output.Length != expectedLength)
            throw new InvalidDataException($"decompressed body length {output.Length} != expected {expectedLength}");
        return output.ToArray();
    }

    private static NativeMovementSnapshot ReadCurrentSnapshot(BinaryReader reader)
    {
        return new NativeMovementSnapshot
        {
            OriginX = reader.ReadSingle(),
            OriginY = reader.ReadSingle(),
            OriginZ = reader.ReadSingle(),
            VelX = reader.ReadSingle(),
            VelY = reader.ReadSingle(),
            VelZ = reader.ReadSingle(),
            Pitch = reader.ReadSingle(),
            Yaw = reader.ReadSingle(),
            Roll = reader.ReadSingle(),
            EntityFlags = reader.ReadUInt32(),
            MoveType = reader.ReadByte(),
            Pad0 = reader.ReadByte(),
            Pad1 = reader.ReadByte(),
            Pad2 = reader.ReadByte(),
            Buttons = reader.ReadUInt64(),
            Buttons1 = reader.ReadUInt64(),
            Buttons2 = reader.ReadUInt64(),
            DuckAmount = reader.ReadSingle(),
            DuckSpeed = reader.ReadSingle(),
            LadderNormalX = reader.ReadSingle(),
            LadderNormalY = reader.ReadSingle(),
            LadderNormalZ = reader.ReadSingle(),
            Ducked = reader.ReadByte(),
            Ducking = reader.ReadByte(),
            DesiresDuck = reader.ReadByte(),
            ActualMoveType = reader.ReadByte()
        };
    }

    private static ReplayProjectileEvent ReadProjectileEvent(BinaryReader reader)
    {
        var tickIndex = reader.ReadUInt32();
        var weaponDefIndex = reader.ReadInt32();
        var kind = (ReplayProjectileKind)reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        var initialPosition = new ReplayVector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
        var initialVelocity = new ReplayVector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
        var detonationPosition = new ReplayVector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
        return new ReplayProjectileEvent(
            tickIndex,
            kind,
            weaponDefIndex,
            initialPosition,
            initialVelocity,
            detonationPosition);
    }

    private static ReplayHighFidelityMetadata ReadHighFidelityMetadata(byte[] metadataJson)
    {
        var metadata = JsonSerializer.Deserialize<ReplayHighFidelityMetadata>(metadataJson, HifiJsonOptions)
            ?? ReplayHighFidelityMetadata.Empty;
        metadata.Events ??= [];
        metadata.InventorySnapshots ??= [];
        return metadata;
    }

    private static string ReadRecString(BinaryReader reader)
    {
        var len = reader.ReadUInt16();
        var bytes = reader.ReadBytes(len);
        if (bytes.Length != len)
            throw new EndOfStreamException("truncated string in .dtr");
        return Encoding.UTF8.GetString(bytes);
    }
}

internal readonly record struct DtrReplayFile(
    NativeReplayTick[] Ticks,
    ReplayProjectileEvent[] Projectiles,
    ReplayHighFidelityMetadata HighFidelity,
    NativeSubtickMove[] Subticks,
    float TickRate,
    uint PlayStartTickIndex);
