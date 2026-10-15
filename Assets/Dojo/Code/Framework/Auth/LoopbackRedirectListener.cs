using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// Catches the OAuth redirect in the Editor and in Standalone builds, where — unlike a
    /// packaged mobile build — there is no installed app for the OS to hand a custom URI scheme
    /// back to.
    /// </summary>
    /// <remarks>
    /// A loopback HTTP redirect is what RFC 8252 §7.3 itself recommends for native desktop apps,
    /// reserving custom URI schemes for mobile — so this isn't a workaround, it's the other half
    /// of the standard's own advice. A raw <see cref="TcpListener"/> rather than
    /// <see cref="System.Net.HttpListener"/>: <c>HttpListener</c> goes through <c>http.sys</c> and
    /// normally needs an admin-registered URL ACL even for a loopback-only prefix on Windows,
    /// which nobody wants to grant just to run this from the Editor.
    /// </remarks>
    static class LoopbackRedirectListener
    {
        const string LogPrefix = "[WorkOS]";

        /// <summary>
        /// True where a custom URI scheme can't reach this process, so the loopback listener is
        /// what has to catch the redirect instead.
        /// </summary>
        internal static bool IsSupported
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.LinuxPlayer:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Waits for exactly one redirect on <c>127.0.0.1:port</c>, answers it with a page the
        /// player can close, and returns the request's path and query — or null on failure,
        /// cancellation, or timeout.
        /// </summary>
        internal static async Awaitable<string> ListenOnceAsync(
            int port, float timeoutSeconds, CancellationToken cancellationToken)
        {
            TcpListener listener;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{LogPrefix} could not listen on 127.0.0.1:{port} for the OAuth "
                    + $"redirect: {exception.Message}");
                return null;
            }

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                // Stopping the listener is how a pending AcceptTcpClientAsync is interrupted —
                // it has no CancellationToken overload on the API level this project targets.
                using (timeoutCts.Token.Register(() => SafeStop(listener)))
                {
                    TcpClient client;

                    try
                    {
                        client = await listener.AcceptTcpClientAsync();
                    }
                    catch (Exception)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        // Either the timeout fired (Stop() mid-accept) or the OS closed it under
                        // us — both read the same way to the caller: no redirect arrived in time.
                        Debug.LogWarning($"{LogPrefix} sign-in did not complete within "
                            + $"{timeoutSeconds:0}s; carrying on without it.");
                        return null;
                    }
                    finally
                    {
                        SafeStop(listener);
                    }

                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var requestLine = await ReadRequestLineAsync(stream, timeoutCts.Token);
                        await WriteCloseTabResponseAsync(stream, timeoutCts.Token);
                        return requestLine;
                    }
                }
            }
        }

        static void SafeStop(TcpListener listener)
        {
            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
                // Already stopped, or never fully started — either way, nothing left to clean up.
            }
        }

        /// <summary>Reads just the request line — enough to get the path and query off of.</summary>
        static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new List<byte>(1024);
            var single = new byte[1];

            while (buffer.Count < 8192)
            {
                int read;

                try
                {
                    read = await stream.ReadAsync(single, 0, 1, cancellationToken);
                }
                catch (Exception)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                buffer.Add(single[0]);

                var last = buffer.Count - 1;

                if (last > 0 && buffer[last - 1] == (byte)'\r' && buffer[last] == (byte)'\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        /// <summary>
        /// A minimal fixed response — no templating, no static assets — telling the player they
        /// are done with the browser.
        /// </summary>
        static async Task WriteCloseTabResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            const string Html =
                "<html><body>Signed in. You can close this tab and go back to the game.</body></html>";

            var body = Encoding.UTF8.GetBytes(Html);
            var header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Content-Type: text/html; charset=utf-8\r\n"
                + $"Content-Length: {body.Length}\r\n"
                + "Connection: close\r\n\r\n");

            try
            {
                await stream.WriteAsync(header, 0, header.Length, cancellationToken);
                await stream.WriteAsync(body, 0, body.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The browser tab may already be gone by the time this lands; the OAuth code was
                // already captured from the request line, so nothing here is worth failing over.
            }
        }
    }
}
