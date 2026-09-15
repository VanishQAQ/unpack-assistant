using System;
using System.IO;
using System.Text;

namespace LayerUnpacker
{
    // Extra checks for ZIP central-directory metadata, including archives with an MP4 prefix.
    public static class ZipGuard
    {
        static void CheckName(byte[] bytes, bool utf8)
        {
            string name = (utf8 ? new UTF8Encoding(false, true) : Encoding.GetEncoding(437)).GetString(bytes);
            Disk.Under(Path.GetTempPath(), name);
        }
        static void CheckExtra(byte[] extra)
        {
            int pos = 0;
            while (pos + 4 <= extra.Length)
            {
                ushort id = BitConverter.ToUInt16(extra, pos), length = BitConverter.ToUInt16(extra, pos + 2); pos += 4;
                if (pos + length > extra.Length) throw new StopException("失败", "ZIP 扩展字段损坏。");
                if (id == 0x7075 && length >= 5)
                {
                    byte[] name = new byte[length - 5]; Array.Copy(extra, pos + 5, name, 0, name.Length); CheckName(name, true);
                }
                if (id == 0x000d) throw new StopException("失败", "ZIP 含未支持的 Unix 特殊文件字段。");
                pos += length;
            }
            if (pos != extra.Length) throw new StopException("失败", "ZIP 扩展字段长度无效。");
        }
        public static void Check(string path)
        {
            using (var stream = File.OpenRead(path))
                Check(stream, null);
        }
        public static void Check(Stream stream, long[] volumeStarts)
        {
            using (var reader = new BinaryReader(stream))
            {
                int count = (int)Math.Min(stream.Length, 65557); stream.Position = stream.Length - count;
                byte[] tail = reader.ReadBytes(count); int end = -1;
                for (int i = count - 22; i >= 0; i--)
                    if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == count) { end = i; break; }
                if (end < 0) throw new StopException("失败", "ZIP 目录尾部缺失或损坏。");
                ushort disk = BitConverter.ToUInt16(tail, end + 4), cdDisk = BitConverter.ToUInt16(tail, end + 6), diskItems = BitConverter.ToUInt16(tail, end + 8), items = BitConverter.ToUInt16(tail, end + 10);
                uint size = BitConverter.ToUInt32(tail, end + 12), offset = BitConverter.ToUInt32(tail, end + 16);
                if (volumeStarts == null && (disk != 0 || cdDisk != 0 || diskItems != items)) throw new StopException("失败", "这是 ZIP 分卷，请切换到分卷解压模式。");
                if (volumeStarts != null && (disk != volumeStarts.Length - 1 || cdDisk >= volumeStarts.Length || diskItems > items)) throw new StopException("失败", "ZIP 分卷数量或目录编号不完整。");
                if (items == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue) throw new StopException("失败", "首版 ZIP 安全检查暂不支持 ZIP64，请使用 Bandizip 手工处理。");
                long endPosition = stream.Length - count + end;
                long start = volumeStarts == null ? endPosition - size : volumeStarts[cdDisk] + offset;
                if (start < 0 || (volumeStarts == null && offset > start) || start + size != endPosition) throw new StopException("失败", "ZIP 中央目录偏移无效。");
                stream.Position = start;
                for (int index = 0; index < items; index++)
                {
                    if (stream.Position + 46 > endPosition || reader.ReadUInt32() != 0x02014b50) throw new StopException("失败", "ZIP 中央目录格式无法验证。");
                    byte[] header = reader.ReadBytes(42);
                    ushort nameLength = BitConverter.ToUInt16(header, 24), extraLength = BitConverter.ToUInt16(header, 26), commentLength = BitConverter.ToUInt16(header, 28), itemDisk = BitConverter.ToUInt16(header, 30);
                    uint attrs = BitConverter.ToUInt32(header, 34); int type = (int)((attrs >> 16) & 0xf000);
                    if (volumeStarts == null ? itemDisk != 0 : itemDisk >= volumeStarts.Length) throw new StopException("失败", "ZIP 条目的分卷编号无效。");
                    if (type != 0 && type != 0x8000 && type != 0x4000 || (attrs & 0x400) != 0)
                        throw new StopException("失败", "ZIP 含链接或特殊文件，已拒绝解压。");
                    long next = stream.Position + nameLength + extraLength + commentLength;
                    if (next > endPosition) throw new StopException("失败", "ZIP 条目长度无效。");
                    byte[] name = reader.ReadBytes(nameLength); bool utf8 = (BitConverter.ToUInt16(header, 4) & 0x800) != 0;
                    CheckName(name, utf8); CheckExtra(reader.ReadBytes(extraLength));
                    long local = (volumeStarts == null ? start - offset : volumeStarts[itemDisk]) + BitConverter.ToUInt32(header, 38);
                    if (local < 0 || local + 30 > start) throw new StopException("失败", "ZIP 本地文件头偏移无效。");
                    stream.Position = local;
                    if (reader.ReadUInt32() != 0x04034b50) throw new StopException("失败", "ZIP 本地文件头无效。");
                    byte[] localHeader = reader.ReadBytes(26);
                    ushort localLength = BitConverter.ToUInt16(localHeader, 22), localExtra = BitConverter.ToUInt16(localHeader, 24);
                    byte[] localName = reader.ReadBytes(localLength);
                    if (localName.Length != name.Length) throw new StopException("失败", "ZIP 文件头名称不一致。");
                    for (int j = 0; j < name.Length; j++) if (name[j] != localName[j]) throw new StopException("失败", "ZIP 文件头名称不一致。");
                    CheckExtra(reader.ReadBytes(localExtra));
                    stream.Position = next;
                }
                if (stream.Position != endPosition) throw new StopException("失败", "ZIP 中央目录长度不一致。");
            }
        }
    }
}
