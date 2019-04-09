using System;
using System.Text;
using MediaBrowser.Model.Services;

namespace SocketHttpListener
{
    internal abstract class HttpBase
    {
        #region Private Fields

        private QueryParamCollection _headers;
        private Version _version;

        #endregion

        #region Protected Fields

        protected const string CrLf = "\r\n";

        #endregion

        #region Protected Constructors

        protected HttpBase(Version version, QueryParamCollection headers)
        {
            _version = version;
            _headers = headers;
        }

        #endregion

        #region Public Properties

        public QueryParamCollection Headers => _headers;

        public Version ProtocolVersion => _version;

        #endregion

        #region Private Methods

        private static Encoding getEncoding(string contentType)
        {
            if (contentType == null || contentType.Length == 0)
                return Encoding.UTF8;

            var i = contentType.IndexOf("charset=", StringComparison.Ordinal);
            if (i == -1)
                return Encoding.UTF8;

            var charset = contentType.Substring(i + 8);
            i = charset.IndexOf(';');
            if (i != -1)
                charset = charset.Substring(0, i).TrimEnd();

            return Encoding.GetEncoding(charset.Trim('"'));
        }

        #endregion

        #region Public Methods

        public byte[] ToByteArray()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }

        #endregion
    }
}