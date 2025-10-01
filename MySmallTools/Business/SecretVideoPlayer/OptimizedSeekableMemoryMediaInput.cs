using LibVLCSharp.Shared;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 优化版本的支持随机访问的内存媒体输入类
    /// 针对LibVLC在可寻址模式下的高频读取进行了性能优化
    /// </summary>
    public class OptimizedSeekableMemoryMediaInput : MediaInput
    {
        private readonly byte[] _data;
        private long _position;
        private readonly GCHandle _dataHandle;
        private readonly IntPtr _dataPtr;

        public OptimizedSeekableMemoryMediaInput(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _position = 0;
            CanSeek = true;
            
            // 固定内存位置，避免每次Read时的fixed开销
            _dataHandle = GCHandle.Alloc(_data, GCHandleType.Pinned);
            _dataPtr = _dataHandle.AddrOfPinnedObject();
        }

        public OptimizedSeekableMemoryMediaInput(MemoryStream memoryStream)
        {
            if (memoryStream == null)
                throw new ArgumentNullException(nameof(memoryStream));
            
            _data = memoryStream.ToArray();
            _position = 0;
            CanSeek = true;
            
            // 固定内存位置，避免每次Read时的fixed开销
            _dataHandle = GCHandle.Alloc(_data, GCHandleType.Pinned);
            _dataPtr = _dataHandle.AddrOfPinnedObject();
        }

        public override bool Open(out ulong size)
        {
            size = (ulong)_data.Length;
            _position = 0;
            return true;
        }

        public override unsafe int Read(IntPtr buf, uint len)
        {
            // 使用局部变量避免字段访问开销
            var currentPosition = _position;
            var dataLength = _data.Length;
            
            if (currentPosition >= dataLength)
                return 0; // EOF

            var bytesToRead = (int)Math.Min(len, dataLength - currentPosition);
            if (bytesToRead <= 0)
                return 0;

            // 使用预固定的指针，避免每次fixed的开销
            var sourcePtr = _dataPtr + (int)currentPosition;
            
            // 使用Buffer.MemoryCopy进行高效复制
            unsafe
            {
                Buffer.MemoryCopy(sourcePtr.ToPointer(), buf.ToPointer(), len, bytesToRead);
            }
            
            // 原子性更新位置（对于单线程访问足够安全）
            _position = currentPosition + bytesToRead;
            
            return bytesToRead;
        }

        public override bool Seek(ulong offset)
        {
            if (offset > (ulong)_data.Length)
                return false;

            _position = (long)offset;
            return true;
        }

        public override void Close()
        {
            _position = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_dataHandle.IsAllocated)
                {
                    _dataHandle.Free();
                }
                _position = 0;
            }
            base.Dispose(disposing);
        }
    }
}