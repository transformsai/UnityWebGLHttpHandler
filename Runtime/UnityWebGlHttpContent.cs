using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TransformsAI.Unity.WebGL.Interop;
using TransformsAI.Unity.WebGL.Interop.Internal;
using TransformsAI.Unity.WebGL.Interop.Types;

namespace TransformsAI.Unity.WebGL
{
    internal sealed class UnityWebGlHttpContent : HttpContent
    {
        private byte[] _data;
        private readonly UnityWebGlFetchResponse _status;

        public UnityWebGlHttpContent(UnityWebGlFetchResponse status)
        {
            _status = status ?? throw new ArgumentNullException(nameof(status));
        }

        private async Task<byte[]> GetResponseData(CancellationToken cancellationToken)
        {
            if (_data != null)
            {
                return _data;
            }
            try
            {
                var dataBufferVal = await _status.ArrayBuffer().Task.ConfigureAwait(continueOnCapturedContext: true);

                using var dataBuffer = dataBufferVal.As<JsObject>();
                using var dataBinView = JsRuntime.CreateHostObject("Uint8Array", dataBuffer).As<JsTypedArray>();
                _data = dataBinView.GetDataCopy<byte>();
                _status.Dispose();
            }
            catch (JsException jse)
            {
                throw UnityWebGlHttpHandler.TranslateJsException(jse, cancellationToken);
            }

            return _data;
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            byte[] data = await GetResponseData(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
            return new MemoryStream(data, writable: false);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
            SerializeToStreamAsync(stream, CancellationToken.None);

        private async Task SerializeToStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            var data = await GetResponseData(cancellationToken).ConfigureAwait(continueOnCapturedContext: true);
            await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: true);
        }
        protected override bool TryComputeLength(out long length)
        {
            if (_data != null)
            {
                length = _data.Length;
                return true;
            }

            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            _status?.Dispose();
            base.Dispose(disposing);
        }
    }
}
