using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using MediaBrowser.Model.Services;

namespace SocketHttpListener.Net
{
    [ComVisible(true)]
    public class WebHeaderCollection : QueryParamCollection
    {
        [Flags]
        internal enum HeaderInfo
        {
            Request = 1,
            Response = 1 << 1,
            MultiValue = 1 << 10
        }

        static readonly bool[] allowed_chars = {
            false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, true, false, true, true, true, true, false, false, false, true,
            true, false, true, true, false, true, true, true, true, true, true, true, true, true, true, false,
            false, false, false, false, false, false, true, true, true, true, true, true, true, true, true,
            true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true,
            false, false, false, true, true, true, true, true, true, true, true, true, true, true, true, true,
            true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true,
            false, true, false
        };

        static readonly Dictionary<string, HeaderInfo> headers;

        static WebHeaderCollection()
        {
            headers = new Dictionary<string, HeaderInfo>(StringComparer.OrdinalIgnoreCase) {
                { "Allow", HeaderInfo.MultiValue },
                { "Accept", HeaderInfo.Request | HeaderInfo.MultiValue },
                { "Accept-Charset", HeaderInfo.MultiValue },
                { "Accept-Encoding", HeaderInfo.MultiValue },
                { "Accept-Language", HeaderInfo.MultiValue },
                { "Accept-Ranges", HeaderInfo.MultiValue },
                { "Age", HeaderInfo.Response },
                { "Authorization", HeaderInfo.MultiValue },
                { "Cache-Control", HeaderInfo.MultiValue },
                { "Cookie", HeaderInfo.MultiValue },
                { "Connection", HeaderInfo.Request | HeaderInfo.MultiValue },
                { "Content-Encoding", HeaderInfo.MultiValue },
                { "Content-Length", HeaderInfo.Request | HeaderInfo.Response },
                { "Content-Type", HeaderInfo.Request },
                { "Content-Language", HeaderInfo.MultiValue },
                { "Date", HeaderInfo.Request },
                { "Expect", HeaderInfo.Request | HeaderInfo.MultiValue},
                { "Host", HeaderInfo.Request },
                { "If-Match", HeaderInfo.MultiValue },
                { "If-Modified-Since", HeaderInfo.Request },
                { "If-None-Match", HeaderInfo.MultiValue },
                { "Keep-Alive", HeaderInfo.Response },
                { "Pragma", HeaderInfo.MultiValue },
                { "Proxy-Authenticate", HeaderInfo.MultiValue },
                { "Proxy-Authorization", HeaderInfo.MultiValue },
                { "Proxy-Connection", HeaderInfo.Request | HeaderInfo.MultiValue },
                { "Range", HeaderInfo.Request | HeaderInfo.MultiValue },
                { "Referer", HeaderInfo.Request },
                { "Set-Cookie", HeaderInfo.MultiValue },
                { "Set-Cookie2", HeaderInfo.MultiValue },
                { "Server", HeaderInfo.Response },
                { "TE", HeaderInfo.MultiValue },
                { "Trailer", HeaderInfo.MultiValue },
                { "Transfer-Encoding", HeaderInfo.Request | HeaderInfo.Response | HeaderInfo.MultiValue },
                { "Translate", HeaderInfo.Request | HeaderInfo.Response },
                { "Upgrade", HeaderInfo.MultiValue },
                { "User-Agent", HeaderInfo.Request },
                { "Vary", HeaderInfo.MultiValue },
                { "Via", HeaderInfo.MultiValue },
                { "Warning", HeaderInfo.MultiValue },
                { "WWW-Authenticate", HeaderInfo.Response | HeaderInfo. MultiValue },
                { "SecWebSocketAccept",  HeaderInfo.Response },
                { "SecWebSocketExtensions", HeaderInfo.Request | HeaderInfo.Response | HeaderInfo. MultiValue },
                { "SecWebSocketKey", HeaderInfo.Request },
                { "Sec-WebSocket-Protocol", HeaderInfo.Request | HeaderInfo.Response | HeaderInfo. MultiValue },
                { "SecWebSocketVersion", HeaderInfo.Response | HeaderInfo. MultiValue }
            };
        }

        // Methods

        public void Add(string header)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            int pos = header.IndexOf(':');
            if (pos == -1)
                throw new ArgumentException("no colon found", nameof(header));

            this.Add(header.Substring(0, pos), header.Substring(pos + 1));
        }

        public override void Add(string name, string value)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            this.AddWithoutValidate(name, value);
        }

        protected void AddWithoutValidate(string headerName, string headerValue)
        {
            if (!IsHeaderName(headerName))
                throw new ArgumentException("invalid header name: " + headerName, nameof(headerName));
            if (headerValue == null)
                headerValue = string.Empty;
            else
                headerValue = headerValue.Trim();
            if (!IsHeaderValue(headerValue))
                throw new ArgumentException("invalid header value: " + headerValue, nameof(headerValue));

            AddValue(headerName, headerValue);
        }

        internal void AddValue(string headerName, string headerValue)
        {
            base.Add(headerName, headerValue);
        }

        internal List<string> GetValues_internal(string header, bool split)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));

            var values = base.GetValues(header);
            if (values == null || values.Count == 0)
                return null;

            if (split && IsMultiValue(header))
            {
                List<string> separated = null;
                foreach (var value in values)
                {
                    if (value.IndexOf(',') < 0)
                    {
                        if (separated != null)
                            separated.Add(value);

                        continue;
                    }

                    if (separated == null)
                    {
                        separated = new List<string>(values.Count + 1);
                        foreach (var v in values)
                        {
                            if (v == value)
                                break;

                            separated.Add(v);
                        }
                    }

                    var slices = value.Split(',');
                    var slices_length = slices.Length;
                    if (value[value.Length - 1] == ',')
                        --slices_length;

                    for (int i = 0; i < slices_length; ++i)
                    {
                        separated.Add(slices[i].Trim());
                    }
                }

                if (separated != null)
                    return separated;
            }

            return values;
        }

        public override List<string> GetValues(string header)
        {
            return GetValues_internal(header, true);
        }

        public override string[] GetValues(int index)
        {
            string[] values = base.GetValues(index);

            if (values == null || values.Length == 0)
            {
                return null;
            }

            return values;
        }

        public static bool IsRestricted(string headerName)
        {
            return IsRestricted(headerName, false);
        }

        public static bool IsRestricted(string headerName, bool response)
        {
            if (headerName == null)
                throw new ArgumentNullException(nameof(headerName));

            if (headerName.Length == 0)
                throw new ArgumentException("empty string", nameof(headerName));

            if (!IsHeaderName(headerName))
                throw new ArgumentException("Invalid character in header");

            if (!headers.TryGetValue(headerName, out HeaderInfo info))
                return false;

            var flag = response ? HeaderInfo.Response : HeaderInfo.Request;
            return (info & flag) != 0;
        }

        public override void Set(string name, string value)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (!IsHeaderName(name))
                throw new ArgumentException("invalid header name");
            if (value == null)
                value = string.Empty;
            else
                value = value.Trim();
            if (!IsHeaderValue(value))
                throw new ArgumentException("invalid header value");

            base.Set(name, value);
        }

        internal string ToStringMultiValue()
        {
            var sb = new StringBuilder();

            int count = base.Count;
            for (int i = 0; i < count; i++)
            {
                string key = GetKey(i);
                if (IsMultiValue(key))
                {
                    foreach (string v in GetValues(i))
                    {
                        sb.Append(key)
                          .Append(": ")
                          .Append(v)
                          .Append("\r\n");
                    }
                }
                else
                {
                    sb.Append(key)
                      .Append(": ")
                      .Append(Get(i))
                      .Append("\r\n");
                }
            }
            return sb.Append("\r\n").ToString();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            int count = base.Count;
            for (int i = 0; i < count; i++)
                sb.Append(GetKey(i))
                  .Append(": ")
                  .Append(Get(i))
                  .Append("\r\n");

            return sb.Append("\r\n").ToString();
        }


        // Internal Methods

        // With this we don't check for invalid characters in header. See bug #55994.
        internal void SetInternal(string header)
        {
            int pos = header.IndexOf(':');
            if (pos == -1)
                throw new ArgumentException("no colon found", nameof(header));

            SetInternal(header.Substring(0, pos), header.Substring(pos + 1));
        }

        internal void SetInternal(string name, string value)
        {
            if (value == null)
                value = string.Empty;
            else
                value = value.Trim();
            if (!IsHeaderValue(value))
                throw new ArgumentException("invalid header value");

            if (IsMultiValue(name))
            {
                base.Add(name, value);
            }
            else
            {
                base.Remove(name);
                base.Set(name, value);
            }
        }

        internal static bool IsMultiValue(string headerName)
        {
            if (headerName == null)
                return false;

            return headers.TryGetValue(headerName, out HeaderInfo info) && (info & HeaderInfo.MultiValue) != 0;
        }

        internal static bool IsHeaderValue(string value)
        {
            // TEXT any 8 bit value except CTL's (0-31 and 127)
            //      but including \r\n space and \t
            //      after a newline at least one space or \t must follow
            //      certain header fields allow comments ()

            int len = value.Length;
            for (int i = 0; i < len; i++)
            {
                char c = value[i];
                if (c == 127)
                    return false;
                if (c < 0x20 && (c != '\r' && c != '\n' && c != '\t'))
                    return false;
                if (c == '\n' && ++i < len)
                {
                    c = value[i];
                    if (c != ' ' && c != '\t')
                        return false;
                }
            }

            return true;
        }

        internal static bool IsHeaderName(string name)
        {
            if (name == null || name.Length == 0)
                return false;

            int len = name.Length;
            for (int i = 0; i < len; i++)
            {
                char c = name[i];
                if (c > 126 || !allowed_chars[c])
                    return false;
            }

            return true;
        }
    }
}