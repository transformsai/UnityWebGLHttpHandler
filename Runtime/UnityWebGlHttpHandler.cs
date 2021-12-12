// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JsInterop;
using JsInterop.Internal;
using JsInterop.Types;
using Runtime = JsInterop.Runtime;

// **Note** on `Task.ConfigureAwait(continueOnCapturedContext: true)` for the WebAssembly Browser.
// The current implementation of WebAssembly for the Browser does not have a SynchronizationContext nor a Scheduler
// thus forcing the callbacks to run on the main browser thread.  When threading is eventually implemented using
// emscripten's threading model of remote worker threads, via SharedArrayBuffer, any API calls will have to be
// remoted back to the main thread.  Most APIs only work on the main browser thread.
// During discussions the concensus has been that it will not matter right now which value is used for ConfigureAwait
// we should put this in place now.
internal sealed class UnityWebGlHttpHandler : HttpMessageHandler
{
    // This partial implementation contains members common to Browser WebAssembly running on .NET Core.
    private static readonly JsValue s_fetch = Runtime.GetGlobalValue("fetch");
    private static readonly JsValue s_window = Runtime.GetGlobalValue("window");

    private static readonly string EnableStreamingResponse = ("WebAssemblyEnableStreamingResponse");
    private static readonly string FetchOptions = ("WebAssemblyFetchOptions");
    private bool _allowAutoRedirect = true;
    // flag to determine if the _allowAutoRedirect was explicitly set or not.
    private bool _isAllowAutoRedirectTouched;

    /// <summary>
    /// Gets whether the current Browser supports streaming responses
    /// </summary>
    private static bool StreamingSupported { get; } = GetIsStreamingSupported();
    private static bool GetIsStreamingSupported()
    {
        using var streamingSupported = Runtime.CreateFunction("return typeof Response !== 'undefined' && 'body' in Response.prototype && typeof ReadableStream === 'function'");
        return streamingSupported.Call();
    }

    public bool UseCookies
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public CookieContainer CookieContainer
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public DecompressionMethods AutomaticDecompression
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool UseProxy
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public IWebProxy Proxy
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public ICredentials DefaultProxyCredentials
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool PreAuthenticate
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public ICredentials Credentials
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool AllowAutoRedirect
    {
        get => _allowAutoRedirect;
        set
        {
            _allowAutoRedirect = value;
            _isAllowAutoRedirectTouched = true;
        }
    }

    public int MaxAutomaticRedirections
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public int MaxConnectionsPerServer
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public int MaxResponseHeadersLength
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }


    public const bool SupportsAutomaticDecompression = false;
    public const bool SupportsProxy = false;
    public const bool SupportsRedirectConfiguration = true;

    private Dictionary<string, object> _properties;
    public IDictionary<string, object> Properties => _properties ??= new Dictionary<string, object>();

    public event Action<HttpRequestMessage> BeforeSend;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        BeforeSend?.Invoke(request);
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var requestObject = Runtime.CreateObject();


            if (request.Properties.TryGetValue(FetchOptions, out var fetchOptions))
            {
                if (fetchOptions is IDictionary d) requestObject.Populate(d);
                else throw new NotSupportedException("Request options must be a dictionary");
            }

            requestObject.SetProp("method", request.Method.Method);

            // Only set if property was specifically modified and is not default value
            if (_isAllowAutoRedirectTouched)
            {
                // Allowing or Disallowing redirects.
                // Here we will set redirect to `manual` instead of error if AllowAutoRedirect is
                // false so there is no exception thrown
                //
                // https://developer.mozilla.org/en-US/docs/Web/API/Response/type
                //
                // other issues from whatwg/fetch:
                //
                // https://github.com/whatwg/fetch/issues/763
                // https://github.com/whatwg/fetch/issues/601
                requestObject.SetProp("redirect", AllowAutoRedirect ? "follow" : "manual");
            }

            // We need to check for body content
            if (request.Content != null)
            {
                if (request.Content is StringContent)
                {
                    requestObject.SetProp("body",
                        await request.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: true));
                }
                else
                {
                    var bytes = await request.Content.ReadAsByteArrayAsync()
                        .ConfigureAwait(continueOnCapturedContext: true);

                    using (var uint8Buffer = Runtime.CreateSharedTypedArray(bytes))
                    {
                        requestObject.SetProp("body", uint8Buffer);
                    }
                }
            }

            // Process headers
            // Cors has its own restrictions on headers.
            // https://developer.mozilla.org/en-US/docs/Web/API/Headers
            using (var jsHeaders = Runtime.CreateHostObject("Headers").As<JsObject>())
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                {
                    foreach (string value in header.Value)
                    {
                        jsHeaders.Invoke("append", header.Key, value);
                    }
                }

                if (request.Content != null)
                {
                    foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    {
                        foreach (string value in header.Value)
                        {
                            jsHeaders.Invoke("append", header.Key, value);
                        }
                    }
                }

                requestObject.SetProp("headers", jsHeaders);
            }

            UnityWebGlHttpReadStream unityWebGlHttpReadStream = null;

            var abortController = Runtime.CreateHostObject("AbortController").As<JsObject>();
            var signal = abortController.GetProp("signal");
            requestObject.SetProp("signal", signal);

            CancellationTokenSource abortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenRegistration abortRegistration = abortCts.Token.Register((Action)(() =>
           {
               if (abortController)
               {
                   abortController.Invoke("abort");
                   abortController.Dispose();
               }

               unityWebGlHttpReadStream?.Dispose();
               abortCts.Dispose();
           }));

            var args = Runtime.CreateArray();
            if (request.RequestUri != null)
            {
                args.Add(request.RequestUri.ToString());
                args.Add(requestObject);
            }

            requestObject.Dispose();

            var response = s_fetch.Invoke("apply", s_window, args).As<JsPromise>();
            args.Dispose();
            if (response == null)
                throw new Exception("SR.net_http_marshalling_response_promise_from_fetch");

            var t = await response.Task.ConfigureAwait(continueOnCapturedContext: true);
            var fetchResponse = t.As<JsObject>();

            var status = new UnityWebGlFetchResponse(fetchResponse, abortController, abortCts, abortRegistration);
            HttpResponseMessage httpResponse = new HttpResponseMessage((HttpStatusCode)status.Status);
            httpResponse.RequestMessage = request;

            // Here we will set the ReasonPhrase so that it can be evaluated later.
            // We do not have a status code but this will signal some type of what happened
            // after interrogating the status code for success or not i.e. IsSuccessStatusCode
            //
            // https://developer.mozilla.org/en-US/docs/Web/API/Response/type
            // opaqueredirect: The fetch request was made with redirect: "manual".
            // The Response's status is 0, headers are empty, body is null and trailer is empty.
            if (status.ResponseType == "opaqueredirect")
            {
                httpResponse.ReasonPhrase = (status.ResponseType);
            }

            bool streamingEnabled = false;
            if (StreamingSupported)
            {
                if (request.Properties.TryGetValue(EnableStreamingResponse, out var streamingEnabledObj))
                    streamingEnabled = (bool)streamingEnabledObj;
            }

            httpResponse.Content = streamingEnabled
                ? new StreamContent(unityWebGlHttpReadStream = new UnityWebGlHttpReadStream(status))
                : (HttpContent)new UnityWebGlHttpContent(status);

            // Fill the response headers
            // CORS will only allow access to certain headers.
            // If a request is made for a resource on another origin which returns the CORs headers, then the type is cors.
            // cors and basic responses are almost identical except that a cors response restricts the headers you can view to
            // `Cache-Control`, `Content-Language`, `Content-Type`, `Expires`, `Last-Modified`, and `Pragma`.
            // View more information https://developers.google.com/web/updates/2015/03/introduction-to-fetch#response_types
            //
            // Note: Some of the headers may not even be valid header types in .NET thus we use TryAddWithoutValidation
            using (JsObject respHeaders = status.Headers)
            {
                if (respHeaders != null)
                {
                    using (var entriesIterator = respHeaders.Invoke("entries").As<JsObject>())
                    {
                        JsObject nextResult = null;
                        try
                        {
                            nextResult = entriesIterator.Invoke("next").As<JsObject>();
                            while (!nextResult.GetProp("done"))
                            {
                                using (var resultValue = nextResult.GetProp("value").As<JsArray>())
                                {
                                    var name = (string)resultValue[0];
                                    var value = (string)resultValue[1];
                                    if (!httpResponse.Headers.TryAddWithoutValidation(name, value))
                                    {
                                        httpResponse.Content.Headers.TryAddWithoutValidation(name, value);
                                    }
                                }

                                nextResult?.Dispose();
                                nextResult = entriesIterator.Invoke("next").As<JsObject>();
                            }
                        }
                        finally
                        {
                            nextResult?.Dispose();
                        }
                    }
                }
            }


            return httpResponse;

        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Request cancelled", oce, cancellationToken);
        }
        catch (JsException jse)
        {
            throw TranslateJSException(jse, cancellationToken);
        }
    }

    internal static Exception TranslateJSException(JsException jse, CancellationToken cancellationToken)
    {
        if (jse.Message.StartsWith("AbortError", StringComparison.Ordinal))
        {
            return new OperationCanceledException("Request cancelled", jse);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException("Request cancelled", jse, cancellationToken);
        }
        return new HttpRequestException(jse.Message, jse);
    }


}
