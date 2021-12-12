using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JsInterop.Internal;
using JsInterop.Types;
internal sealed class UnityWebGlHttpReadStream : Stream
{
    private UnityWebGlFetchResponse _status;
    private JsObject _reader;

    private byte[] _bufferedBytes;
    private int _position;

    public UnityWebGlHttpReadStream(UnityWebGlFetchResponse status)
    {
        _status = status;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_reader == null)
        {
            // If we've read everything, then _reader and _status will be null
            if (_status == null)
            {
                return 0;
            }

            try
            {
                using (JsObject body = _status.Body)
                {
                    _reader = body.Invoke("getReader").As<JsObject>();
                }
            }
            catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Request cancelled:", oce, cancellationToken);
            }
            catch (JsException jse)
            {
                throw UnityWebGlHttpHandler.TranslateJSException(jse, cancellationToken);
            }
        }

        if (_bufferedBytes != null && _position < _bufferedBytes.Length)
        {
            return ReadBuffered();
        }

        try
        {
            var t = _reader.Invoke("read").As<JsPromise>();
            var readVal = await t.Task.ConfigureAwait(continueOnCapturedContext: true);
            using (var read = readVal.As<JsObject>())
            {
                if ((bool)read.GetProp("done"))
                {
                    _reader.Dispose();
                    _reader = null;

                    _status?.Dispose();
                    _status = null;
                    return 0;
                }

                _position = 0;
                // value for fetch streams is a Uint8Array
                using (var binValue = read.GetProp("value").As<JsTypedArray>())
                    _bufferedBytes = binValue.GetDataCopy<byte>();
            }
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Request cancelled:", oce, cancellationToken);
        }
        catch (JsException jse)
        {
            throw UnityWebGlHttpHandler.TranslateJSException(jse, cancellationToken);
        }

        return ReadBuffered();

        int ReadBuffered()
        {
            int n = Math.Min(_bufferedBytes.Length - _position, count);
            if (n <= 0)
            {
                return 0;
            }

            Array.Copy(_bufferedBytes, _position, buffer, offset, n);
            _position += n;

            return n;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _reader?.Dispose();
        _status?.Dispose();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("SR.net_http_synchronous_reads_not_supported");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
