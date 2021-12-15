using System;
using System.Threading;
using TransformsAI.Unity.WebGL.Interop.Types;

namespace TransformsAI.Unity.WebGL
{
    internal sealed class UnityWebGlFetchResponse : IDisposable
    {
        private readonly JsObject _fetchResponse;
        private readonly JsObject _abortController;
        private readonly CancellationTokenSource _abortCts;
        private CancellationTokenRegistration _abortRegistration;
        private bool _isDisposed;

        public UnityWebGlFetchResponse(JsObject fetchResponse, JsObject abortController, CancellationTokenSource abortCts, CancellationTokenRegistration abortRegistration)
        {
            _fetchResponse = fetchResponse ?? throw new ArgumentNullException(nameof(fetchResponse));
            _abortController = abortController ?? throw new ArgumentNullException(nameof(abortController));
            _abortCts = abortCts;
            _abortRegistration = abortRegistration;
        }

        public bool IsOK => _fetchResponse.GetProp("ok");
        public bool IsRedirected => _fetchResponse.GetProp("redirected");
        public int Status => (int)_fetchResponse.GetProp("status");
        public string StatusText => (string)_fetchResponse.GetProp("statusText");
        public string ResponseType => (string)_fetchResponse.GetProp("type");
        public string Url => (string)_fetchResponse.GetProp("url");
        public bool IsBodyUsed => _fetchResponse.GetProp("bodyUsed");
        public JsObject Headers => _fetchResponse.GetProp("headers").As<JsObject>();
        public JsObject Body => _fetchResponse.GetProp("body").As<JsObject>();

        public JsPromise ArrayBuffer() => _fetchResponse.Invoke("arrayBuffer").As<JsPromise>();
        public JsPromise Text() => _fetchResponse.Invoke("text").As<JsPromise>();
        public JsPromise JSON() => _fetchResponse.Invoke("json").As<JsPromise>();

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _abortCts.Dispose();
            _abortRegistration.Dispose();

            _fetchResponse?.Dispose();
            _abortController?.Dispose();
        }
    }
}
