using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private sealed class BotHiderMemoryProbe : IDisposable
    {
        private const string MappingName = "CS2BotHider_Slots";
        private const string PosixMappingPath = "/dev/shm/CS2BotHider_Slots";
        private const uint Magic = 0x44494842;
        private const int MaxSlots = 64;
        private const int TotalSize = 16384;
        private const int OffMagic = 0;
        private const int OffSlotState = 16;

        private MemoryMappedFile? _memory;
        private MemoryMappedViewAccessor? _view;

        public bool IsAvailable()
            => TryConnect();

        public bool IsManagedBot(int slot)
        {
            if (slot < 0 || slot >= MaxSlots)
                return false;
            if (!TryConnect())
                return false;

            return _view!.ReadByte(OffSlotState + slot) != 0;
        }

        private bool TryConnect()
        {
            if (_view != null)
                return true;

            try
            {
                _memory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read)
                    : MemoryMappedFile.CreateFromFile(
                        PosixMappingPath,
                        FileMode.Open,
                        null,
                        TotalSize,
                        MemoryMappedFileAccess.Read);
                _view = _memory.CreateViewAccessor(0, TotalSize, MemoryMappedFileAccess.Read);
                if (_view.ReadUInt32(OffMagic) == Magic)
                    return true;
            }
            catch
            {
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            _view?.Dispose();
            _memory?.Dispose();
            _view = null;
            _memory = null;
        }
    }
}
