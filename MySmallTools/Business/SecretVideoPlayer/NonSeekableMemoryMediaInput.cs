using LibVLCSharp.Shared;
using System;
using System.IO;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 非可寻址的内存媒体输入类
    /// 测试LibVLC在流式模式下的性能表现
    /// </summary>
    public class NonSeekableMemoryMediaInput : MediaInput
    {
        private readonly byte[] _data;
        private long _position;
        private readonly object _lockObject = new object();

        public NonSeekableMemoryMediaInput(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _position = 0;
            CanSeek = false; // 关键：设置为不可寻址
        }

        public NonSeekableMemoryMediaInput(MemoryStream memoryStream)
        {
            if (memoryStream == null)
                throw new ArgumentNullException(nameof(memoryStream));
            
            _data = memoryStream.ToArray();
            _position = 0;
            CanSeek = false; // 关键：设置为不可寻址
        }

        /// <summary>
        /// LibVLC调用此方法打开媒体
        /// </summary>
        /// <param name="size">必须填入媒体长度</param>
        /// <returns>成功返回true，失败返回false</returns>
        public override bool Open(out ulong size)
        {
            size = (ulong)_data.Length;
            _position = 0;
            return true;
        }

        public override unsafe int Read(IntPtr buf, uint len)
        {
            lock (_lockObject)
            {
                if (_position >= _data.Length)
                    return 0; // EOF

                var bytesToRead = (int)Math.Min(len, _data.Length - _position);
                if (bytesToRead <= 0)
                    return 0;

                // 将数据复制到LibVLC提供的缓冲区
                fixed (byte* dataPtr = &_data[_position])
                {
                    Buffer.MemoryCopy(dataPtr, buf.ToPointer(), len, bytesToRead);
                }

                _position += bytesToRead;
                return bytesToRead;
            }
        }

        /// <summary>
        /// 非可寻址模式下不支持Seek操作
        /// </summary>
        public override bool Seek(ulong offset)
        {
            // 非可寻址模式下返回false
            return false;
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