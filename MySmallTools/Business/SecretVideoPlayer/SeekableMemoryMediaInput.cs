using LibVLCSharp.Shared;
using System;
using System.IO;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 支持随机访问的内存媒体输入类
    /// 通过实现seeking功能来解决MemoryStream在LibVLC中不可寻址的问题
    /// </summary>
    public class SeekableMemoryMediaInput : MediaInput
    {
        private readonly byte[] _data;
        private ulong _position;
        private readonly object _lockObject = new object();

        private ulong allSize = 0;

        private FullVideoDecryptor _decryptor;

        public SeekableMemoryMediaInput(FullVideoDecryptor _decryptor)
        {
            var memoryStream = (MemoryStream)_decryptor.DecryptedStream;
            this._decryptor = _decryptor;
            _data = memoryStream?.ToArray() ?? [];
            _position = 0;
            CanSeek = true;
        }

        /// <summary>
        /// LibVLC调用此方法打开媒体
        /// </summary>
        /// <param name="size">必须填入媒体长度</param>
        /// <returns>成功返回true，失败返回false</returns>
        public override bool Open(out ulong size)
        {
            size = (ulong)(_decryptor?.VideoInfo?.Metadata?.FileSize ?? 0);
            allSize = size;
            _position = 0;
            return true;
        }

        public override unsafe int Read(IntPtr buf, uint len)
        {
            lock (_lockObject)
            {
                if (_position >= allSize)
                    return 0; // EOF

                var bytesToRead = (uint)Math.Min(len, allSize - _position);
                if (bytesToRead <= 0)
                    return 0;

                // 将数据复制到LibVLC提供的缓冲区
                fixed (byte* dataPtr = &_data[_position])
                {
                    Buffer.MemoryCopy(dataPtr, buf.ToPointer(), len, bytesToRead);
                }

                _position += bytesToRead;
                return (int)bytesToRead;
            }
        }

        public override bool Seek(ulong offset)
        {
            lock (_lockObject)
            {
                if (offset > allSize)
                    return false;

                _position = offset;
                return true;
            }
        }

        /// <summary>
        /// LibVLC调用此方法关闭媒体
        /// </summary>
        public override void Close()
        {
            lock (_lockObject)
            {
                _position = 0; // 重置位置
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 清理资源
                _position = 0;
            }

            base.Dispose(disposing);
        }
    }
}