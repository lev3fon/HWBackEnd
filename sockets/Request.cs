using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;

namespace Sockets
{
    public class Request
    {
        public const string HttpLineSeparator = "\r\n";

        public string Method;
        public string RequestUri;
        public string HttpVersion;
        public List<Header> Headers;
        public byte[] MessageBody;

        public class Header
        {
            public Header(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public readonly string Name;
            public readonly string Value;
        }

        // Структура http-запроса: https://www.w3.org/Protocols/rfc2616/rfc2616-sec5.html
        public static Request StupidParse(byte[] requestBytes)
        {
            string requestString = Encoding.ASCII.GetString(requestBytes);

            RequestLine requestLine = ParseRequestLine(requestString, out int readCharsCount);

            List<Header> headers = ParseHeaders(requestString, ref readCharsCount);
            if (headers == null)
                return null;

            int readBytesCount = Encoding.ASCII.GetBytes(requestString.Substring(0, readCharsCount)).Length;
            byte[] messageBody = ParseMessageBody(requestBytes, headers, readBytesCount);
            if (messageBody == null)
                return null;

            return new Request
            {
                Method = requestLine.Method,
                RequestUri = requestLine.RequestUri,
                HttpVersion = requestLine.HttpVersion,
                Headers = headers,
                MessageBody = messageBody
            };
        }

        private static RequestLine ParseRequestLine(string requestString, out int readCharsCount)
        {
            readCharsCount = 0;
            int lineEnd = requestString.IndexOf(HttpLineSeparator, StringComparison.InvariantCulture);
            if (lineEnd < 0)
                return null;

            readCharsCount = lineEnd + 2;
            string requestLineString = requestString.Substring(0, lineEnd);
            string[] requestLineParts = requestLineString.Split(' ');
            return new RequestLine(requestLineParts[0], requestLineParts[1], requestLineParts[2]);
        }

        private static List<Header> ParseHeaders(string requestString, ref int readCharsCount)
        {
            int lineStart = readCharsCount;

            List<Header> headers = new List<Header>();
            while (true)
            {
                if (lineStart >= requestString.Length)
                    return null;
                int lineEnd = requestString.IndexOf(HttpLineSeparator, lineStart, StringComparison.InvariantCulture);
                if (lineEnd < 0)
                    return null;
                if (lineStart == lineEnd)
                    break;

                string headerString = requestString.Substring(lineStart, lineEnd - lineStart);
                string[] headerParts = headerString.Split(':');
                headers.Add(new Header(headerParts[0].Trim(), headerParts[1].Trim()));

                lineStart = lineEnd + HttpLineSeparator.Length;
            }

            readCharsCount = lineStart + HttpLineSeparator.Length;
            return headers;
        }

        private static byte[] ParseMessageBody(byte[] requestBytes, List<Header> headers, int readBytesCount)
        {
            int? contentLength = FindContentLength(headers);
            int messageBodyLength = contentLength.HasValue ? contentLength.Value : requestBytes.Length - readBytesCount;
            if (messageBodyLength > requestBytes.Length - readBytesCount)
                return null;

            byte[] messageBody = new byte[messageBodyLength];
            Array.Copy(requestBytes, readBytesCount, messageBody, 0, messageBody.Length);
            return messageBody;
        }

        private static int? FindContentLength(List<Header> headers)
        {
            Header contentLengthHeader = headers.FirstOrDefault(
                h => string.Equals(h.Name, "Content-Length", StringComparison.InvariantCultureIgnoreCase));
            return contentLengthHeader != null ? (int?)int.Parse(contentLengthHeader.Value) : null;
        }

        private class RequestLine
        {
            public RequestLine(string method, string requestUri, string httpVersion)
            {
                Method = method;
                RequestUri = requestUri;
                HttpVersion = httpVersion;
            }

            public readonly string Method;
            public readonly string RequestUri;
            public readonly string HttpVersion;
        }
    }

    [TestFixture]
    public class RequestSpecification
    {
        private const string RequestLine = "GET /users/1 HTTP/1.1\r\n";
        private const string Header1 = "UnknownHeader1: Value1\r\n";
        private const string Header2 = "UnknownHeader2: Value2\r\n";
        private const string Separator = "\r\n";

        private Request.Header requestHeader1;
        private Request.Header requestHeader2;

        private Request CreateRequest()
        {
            return new Request
            {
                Method = "GET",
                HttpVersion = "HTTP/1.1",
                RequestUri = "/users/1",
                Headers = new List<Request.Header>(),
                MessageBody = new byte[0]
            };
        }

        [SetUp]
        public void SetUp()
        {
            requestHeader1 = new Request.Header("UnknownHeader1", "Value1");
            requestHeader2 = new Request.Header("UnknownHeader2", "Value2");
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenEmptyInput()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(""));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenOnlyRequestLine()
        {
            var expected = CreateRequest();
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(RequestLine + Separator));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenNotFinishedRequestLine()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes("GET http://host/users/1 HTTP/1.1"));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenRequestLineWithoutSeparator()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(RequestLine));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenRequestLineAndSingleHeader()
        {
            var input = RequestLine + Header1 + Separator;
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldSucceed_WhenRequestLineAndHeaders()
        {
            var input = RequestLine + Header1 + Header2 + Separator;
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenNotFinishedHeader()
        {
            var input = RequestLine + "UnknownHeader1: Value1";

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenHeadersWithoutSeparator()
        {
            var input = RequestLine + Header1 + Header2;

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenBodyWithoutContentLength()
        {
            var input = RequestLine + Header1 + Header2 + Separator + "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.MessageBody = Encoding.ASCII.GetBytes("just text body");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldSucceed_WhenBodyWithContentLength()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 14\r\n" +
                        Separator +
                        "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.Headers.Add(new Request.Header("Content-Length", "14"));
            expected.MessageBody = Encoding.ASCII.GetBytes("just text body");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldTrimBody_WhenContentLengthLessThanBody()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 10\r\n" +
                        Separator +
                        "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.Headers.Add(new Request.Header("Content-Length", "10"));
            expected.MessageBody = Encoding.ASCII.GetBytes("just text ");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenContentLengthMoreThanBody()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 15\r\n" +
                        Separator +
                        "just text body";

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }
    }
}
