using System;

namespace RandomOrg.CoreApi.Errors
{
    /// <summary>
    /// Exception raised by the RandomOrgClient class when the connection doesn't return a HTTP 200 OK response.
    /// </summary>
    public class RandomOrgBadHTTPResponseException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgBadHTTPResponseException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when its API key's request has exceeded its remaining server bits allowance.
    /// </summary>
    /// <remarks>
    /// If the client is currently issuing large requests it may be possible succeed with smaller requests.Use the client's 
    /// <c>GetBitsLeft()</c> call or access the <c>bits</c> parameter in this class to help determine if an alternative request size is 
    /// appropriate.
    /// </remarks>
    public class RandomOrgInsufficientBitsException : Exception
    {
        /// <summary>
        /// Store for the number of bits remaining.
        /// </summary>
        public readonly int bits = -1;

        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        /// <param name="bits">bits remaining just before error thrown</param>
        public RandomOrgInsufficientBitsException(string message, int bits)
            : base(message)
        {
            this.bits = bits;
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when its API key's server requests allowance has been exceeded.
    /// </summary>
    /// <remarks>
    /// This indicates that a back-off until midnight UTC is in effect, before which no requests will be sent by the 
    /// client as no meaningful server responses will be returned.
    /// </remarks>
    public class RandomOrgInsufficientRequestsException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgInsufficientRequestsException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when the server returns a JSON-RPC Error.
    /// </summary>
    /// <remarks>
    /// See https://api.random.org/json-rpc/4/error-codes
    /// </remarks>
    public class RandomOrgJSONRPCException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgJSONRPCException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when its API key has been stopped. Requests will not complete 
    /// while API key is in the stopped state.
    /// </summary>
    public class RandomOrgKeyNotRunningException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgKeyNotRunningException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when the server returns a RANDOM.ORG Error.
    /// </summary>
    /// <remarks>
    /// See https://api.random.org/json-rpc/4/error-codes
    /// </remarks>
    public class RandomOrgRANDOMORGException : Exception
    {
        /// <summary>
        /// Store for the RANDOM.ORG Error code.
        /// </summary>
        public readonly int code = -1;

        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgRANDOMORGException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Constructs a new exception with the specified detail message and error code.
        /// </summary>
        /// <param name="message">the detail message</param>
        /// <param name="code">the error code, see https://api.random.org/json-rpc/4/error-codes </param>
        public RandomOrgRANDOMORGException(string message, int code)
            : base(message)
        {
            this.code = code;
        }
    }

    /// <summary>
    /// Exception raised by the RandomOrgClient class when its set blocking timeout is exceeded before the 
    /// request can be sent.
    /// </summary>
    public class RandomOrgSendTimeoutException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgSendTimeoutException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception raised when data retrievel from an emtpy <c>RandomOrgCache&lt;T&gt;</c> is attempted. 
    /// </summary>
    public class RandomOrgCacheEmptyException : Exception
    {
        /// <summary>
        /// Constructs a new exception with the specified detail message.
        /// </summary>
        /// <param name="message">the detail message</param>
        public RandomOrgCacheEmptyException(string message)
            : base(message)
        {
        }
    }
}
