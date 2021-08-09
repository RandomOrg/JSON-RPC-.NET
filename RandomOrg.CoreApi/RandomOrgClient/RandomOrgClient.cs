using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RandomOrg.CoreApi.Errors;

namespace RandomOrg.CoreApi
{
    /// <summary>
    /// RandomOrgClient main class through which API functions are accessed.
    /// </summary>
    /// <remarks>
    /// This class provides either serialized or unserialized (determined on class creation) access to both the 
    /// signed and unsigned methods of the RANDOM.ORG API. These are threadsafe and implemented as blocking remote 
    /// procedure calls.
    /// <para/>If requests are to be issued serially a background Thread will maintain a queue of requests to 
    /// process in sequence.
    /// <para/>The class also provides
    /// access to creation of a convenience class, RandomOrgCache, for precaching API 
    /// responses when the request is known in advance.
    /// <para/>This class will only allow the creation of one instance per API key. If an instance of this class 
    /// already exists for a given key, that instance will be returned instead of a new instance.
    /// <para/>This class obeys most of the guidelines set forth <a href="https://api.random.org/json-rpc/4">here</a>. 
    /// All requests respect the server's advisoryDelay returned in any responses, or use 
    /// <see cref="DefaultDelay"/> if no advisoryDelay is returned. If the supplied API key 
    /// is paused, i.e., has exceeded its daily bit/request allowance, this implementation will back off until midnight UTC.
    /// </remarks>
    public class RandomOrgClient
    {
        // Basic RANDOM.ORG API functions https://api.random.org/json-rpc/4/basic
        private const string IntegerMethod = "generateIntegers";
        private const string IntegerSequenceMethod = "generateIntegerSequences";
        private const string DecimalFractionMethod = "generateDecimalFractions";
        private const string GaussianMethod = "generateGaussians";
        private const string StringMethod = "generateStrings";
        private const string UUIDMethod = "generateUUIDs";
        private const string BlobMethod = "generateBlobs";
        private const string GetUsageMethod = "getUsage";

        // Signed RANDOM.ORG API functions https://api.random.org/json-rpc/4/signed
        private const string SignedIntegerMethod = "generateSignedIntegers";
        private const string SignedIntegerSequenceMethod = "generateSignedIntegerSequences";
        private const string SignedDecimalFractionMethod = "generateSignedDecimalFractions";
        private const string SignedGaussianMethod = "generateSignedGaussians";
        private const string SignedStringMethod = "generateSignedStrings";
        private const string SignedUUIDMethod = "generateSignedUUIDs";
        private const string SignedBlobMethod = "generateSignedBlobs";
        private const string VerifySignatureMethod = "verifySignature";
        private const string GetResultMethod = "getResult";
        private const string CreateTicketMethod = "createTickets";
        private const string ListTicketMethod = "listTickets";
        private const string GetTicketMethod = "getTicket";

        // Blob format literals
        /// <summary> Blob format literal (base64).</summary>
        public const string BlobFormatBase64 = "base64";
        /// <summary> Blob format literal (hex).</summary>
        public const string BlobFormatHex = "hex";

        // Default values
        /// <summary> Default value for the <c>replacement</c> parameter (<c>true</c>).</summary>
        public const bool DefaultReplacement = true;
        /// <summary> Default value for the <c>integerBase</c> parameter (<c>10</c>).</summary>
        public const int DefaultIntBase = 10;
        /// <summary> Default value for <c>blockingTimeout</c> parameter (1 day).</summary>
        public const long DefaultBlockingTimeout = 24 * 60 * 60 * 1000;
        /// <summary> Default value for <c>httpTimeout</c> parameter (2 minutes).</summary>
        public const int DefaultHttpTimeout = 120 * 1000;
        /// <summary> Default value for the number of result sets stored in a RandomOrgCache&lt;T&gt; (20)</summary>
        public const int DefaultCacheSize = 20;
        /// <summary> Default value for the number of result sets stored in a small RandomOrgCache&lt;T&gt; (10)</summary>
        public const int DefaultCacheSizeSmall = 10;
        /// <summary> Maximum length for signature verification URL (2,046 characters). </summary>
        public const int MaximumLengthUrl = 2046;

        // Default back-off to use if no advisoryDelay back-off supplied by server (1 second)
        private const int DefaultDelay = 1 * 1000;

        // On request fetch fresh allowance state if current state data is older than this value (1 hour)
        private const int AllowanceStateRefreshSeconds = 3600 * 1000;

        // Default data sizes in bits
        private const int UUIDSize = 122;

        // Maintain a dictionary of API keys and their instances.
        private static readonly Dictionary<string, RandomOrgClient> KeyIndexedInstances = new Dictionary<string, RandomOrgClient>();

        private static readonly HashSet<int> RandomOrgErrors = new HashSet<int>
            { 100, 101, 200, 201, 202, 203, 204, 300, 301, 302, 303, 304, 305, 306, 307,
            400, 401, 402, 403, 404, 405, 420, 421, 422, 423, 424, 425, 500, 32000 };

        private readonly string apiKey;
        private readonly long blockingTimeout;
        private readonly int httpTimeout;
        private readonly bool serialized;

        // Maintain info to obey server advisory delay
        private readonly object advisoryDelayLock = new object();
        private int advisoryDelay = 0;
        private long lastResponseReceivedTime = 0;

        // Maintain usage statistics from server
        private int requestsLeft = -1;
        private int bitsLeft = -1;

        // Back-off info for when API key is detected as not running - probably because key 
        // has exceeded its daily usage limit. Back-off runs until midnight UTC.
        private long backoff = -1;
        private string backoffError;

        private readonly Queue<Dictionary<string, object>> serializedQueue;

        /// <summary>
        /// Ensure only one instance of RandomOrgClient exists per API key. Create a new instance if the supplied 
        /// key isn't already known, otherwise return the previously instantiated one.
        /// </summary>
        /// <param name="apiKey">
        /// apiKey of instance to create/find, obtained from RANDOM.ORG, see <a href="https://api.random.org/api-keys">here</a>
        /// </param>
        /// <param name="blockingTimeout">
        /// maximum time in milliseconds to wait before being allowed to send a request. Note this is a hint not a 
        /// guarantee. The advisory delay from server must always be obeyed. Supply a value of -1 to allow blocking 
        /// forever (optional; default 24*60*60*1000, i.e., 1 day).
        /// </param>
        /// <param name="httpTimeout">
        /// maximum time in milliseconds to wait for the server response to a request (optional; default 120*1000).
        /// </param>
        /// <param name="serialized">
        /// serialized determines whether or not requests from this instance will be added to a Queue and issued 
        /// serially or sent when received, obeying any advisory delay (optional; default true).
        /// </param>
        /// <returns>new instance if instance doesn't already exist for this key, else existing instance</returns>
        public static RandomOrgClient GetRandomOrgClient(string apiKey, long blockingTimeout = DefaultBlockingTimeout, int httpTimeout = DefaultHttpTimeout, bool serialized = true)
        {
            if (KeyIndexedInstances.ContainsKey(apiKey))
            {
                return KeyIndexedInstances[apiKey];
            }
            else
            {
                RandomOrgClient instance = new RandomOrgClient(apiKey, blockingTimeout, httpTimeout, serialized);
                KeyIndexedInstances.Add(apiKey, instance);
                return instance;
            }
        }

        /// <summary>
        /// Constructor. Initialize class and start serialized request sending Thread running if applicable.
        /// </summary>
        /// <param name="apiKey">
        /// apiKey of instance to create/find, obtained from RANDOM.ORG, see <a href="https://api.random.org/api-keys">here</a>.
        /// </param>
        /// <param name="blockingTimeout">
        /// maximum time in milliseconds to wait before being allowed to send a request.Note this is a hint not a 
        /// guarantee. Be advised advisory delay from server must always be obeyed. Supply a value of -1 to allow 
        /// blocking forever (default 24*60*60*1000, i.e., 1 day).
        /// </param>
        /// <param name="httpTimeout">
        /// maximum time in milliseconds to wait for the server response to a request (default 120*1000).
        /// </param>
        /// <param name="serialized">
        /// determines whether or not requests from this instance will be added to a Queue and issued serially or sent 
        /// when received, obeying any advisory delay (default true).
        /// </param>
        private RandomOrgClient(string apiKey, long blockingTimeout, int httpTimeout, bool serialized)
        {
            if (serialized)
            {
                // set up the serialized request Queue and Thread
                this.serializedQueue = new Queue<Dictionary<string, object>>();

                Thread t = new Thread(new ThreadStart(this.ThreadedRequestSending));
                t.Start();
            }

            this.apiKey = apiKey;
            this.blockingTimeout = blockingTimeout;
            this.httpTimeout = httpTimeout;
            this.serialized = serialized;

            try
            {
                this.GetUsage();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Request and return an array of true random integers within a user-defined range from the server. 
        /// See <a href="https://api.random.org/json-rpc/4/basic#generateIntegers">here</a>.
        /// </summary>
        /// <param name="n">the number of random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="min">
        /// the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.
        /// </param>
        /// <param name="max">
        /// the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.
        /// </param>
        /// <param name="replacement">
        /// specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may 
        /// contain duplicate values, otherwise the numbers will all be unique (optional; default true).
        /// </param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>int[] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public int[] GenerateIntegers(int n, int min, int max, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractInts(this.IntegerHelper(n, min, max, replacement: replacement,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return an array of true random integers within a user-defined range from the server. 
        /// See <a href="https://api.random.org/json-rpc/4/basic#generateIntegers">here</a>.
        /// Note: This method returns a <strong>string</strong> array, as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">the number of random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="min">
        /// the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.
        /// </param>
        /// <param name="max">
        /// the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.
        /// </param>
        /// <param name="integerBase">
        /// the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16 (default 10). 
        /// </param>
        /// <param name="replacement">
        /// specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may 
        /// contain duplicate values, otherwise the numbers will all be unique (optional; default true).
        /// </param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>string[] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public string[] GenerateIntegers(int n, int min, int max, int integerBase, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractStrings(this.IntegerHelper(n, min, max, replacement: replacement, integerBase: integerBase,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return uniform sequences of true random integers within user-defined ranges from the server. See 
        /// <a href="https://api.random.org/json-rpc/4/basic#generateIntegerSequences">here</a>.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">the length of each array of random integers requested. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within 
        /// the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within 
        /// the[-1e9, 1e9] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the 
        /// resulting numbers may contain duplicate values, otherwise the numbers will all be unique(optional; default true).</param>
        /// <returns>int[][] of true random integers.</returns>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>int[][] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public int[][] GenerateIntegerSequences(int n, int length, int min, int max, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractIntSequences(this.IntegerSequenceHelper(n, length, min, max, replacement: replacement,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return uniform sequences of true random integers within user-defined ranges from the server. See 
        /// <a href="https://api.random.org/json-rpc/4/basic#generateIntegerSequences">here</a>. 
        /// Note: This method returns a <strong>string</strong> array, as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">the length of each array of random integers requested. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within 
        /// the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within 
        /// the[-1e9, 1e9] range.</param>
        /// <param name="integerBase">the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16 (default 10).</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the 
        /// resulting numbers may contain duplicate values, otherwise the numbers will all be unique(optional; default true).</param>
        /// <returns>string[][] of true random integers.</returns>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>string[][] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public string[][] GenerateIntegerSequences(int n, int length, int min, int max, int integerBase, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractIntSequencesString(this.IntegerSequenceHelper(n, length, min, max, replacement: replacement,
                integerBase: integerBase, pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return uniform or multiform sequences of true random integers within user-defined ranges from the server. 
        /// See <a href="https://api.random.org/json-rpc/4/basic#generateIntegerSequences">here</a>.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">an array with n integers each specifying the length of the sequence identified by its index. 
        /// Each value in the array must be within the [1,1e4] range.</param>
        /// <param name="min">an array with n integers, each specifying the lower boundary of the sequence identified by its 
        /// index. Each value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="max">an array with n integers, each specifying the upper boundary of the sequence identified by its 
        /// index. Each value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="replacement">an array with n Boolean values, each specifying whether the sequence identified 
        /// by its index will be created with or without replacement. If true, the resulting numbers may contain duplicate values, 
        /// otherwise the numbers will all be unique within each sequence (optional; default null, will be handled as an array of length 
        /// n containing true).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>int[][] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public int[][] GenerateIntegerSequences(int n, int[] length, int[] min, int[] max, bool[] replacement = null, JObject pregeneratedRandomization = null)
        {
            return this.ExtractIntSequences(this.IntegerSequenceHelper(n, length, min, max, replacement: replacement,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return uniform or multiform sequences of true random integers within user-defined ranges from the server. 
        /// See <a href="https://api.random.org/json-rpc/4/basic#generateIntegerSequences">here</a>.
        /// Note: This method returns a <strong>string</strong> array, as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">an array with n integers each specifying the length of the sequence identified by its index. 
        /// Each value in the array must be within the [1,1e4] range.</param>
        /// <param name="min">an array with n integers, each specifying the lower boundary of the sequence identified by its 
        /// index. Each value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="max">an array with n integers, each specifying the upper boundary of the sequence identified by its 
        /// index. Each value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="integerBase">an array with n integer values, each specifying the base that will be used to display 
        /// the sequence identified by its index.</param>
        /// <param name="replacement">replacement an array with n Boolean values, each specifying whether the sequence identified 
        /// by its index will be created with or without replacement. If true, the resulting numbers may contain duplicate values, 
        /// otherwise the numbers will all be unique within each sequence (optional; default null, will be handled as an array of length 
        /// n containing true).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>string[][] of true random integers.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public string[][] GenerateIntegerSequences(int n, int[] length, int[] min, int[] max, int[] integerBase, bool[] replacement = null, JObject pregeneratedRandomization = null)
        {
            return this.ExtractIntSequencesString(this.IntegerSequenceHelper(n, length, min, max, replacement: replacement,
                integerBase: integerBase, pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return a list (size n) of true random decimal fractions, from a uniform distribution across the [0, 1] 
        /// interval with a user-defined number of decimal places from the server. See 
        /// <a href="https://api.random.org/json-rpc/4/basic#generateDecimalFractions">here</a>.
        /// </summary>
        /// <param name="n">how many random decimal fractions you need. Must be within the [1,1e4] range.</param>
        /// <param name="decimalPlaces">the number of decimal places to use. Must be within the [1,20] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, 
        /// the resulting numbers may contain duplicate values, otherwise the numbers will all be unique(optional; default true).</param>
        /// <returns>double[] of true random decimal fractions.</returns>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>double[] of true random decimal fractions.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public double[] GenerateDecimalFractions(int n, int decimalPlaces, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractDoubles(this.DecimalFractionHelper(n, decimalPlaces, replacement,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return a list (size n) of true random numbers from a Gaussian distribution (also known as a normal 
        /// distribution). The form uses a Box-Muller Transform to generate the Gaussian distribution from uniformly 
        /// distributed numbers. See <a href="https://api.random.org/json-rpc/4/basic#generateGaussians">here</a>.
        /// </summary>
        /// <param name="n">how many random numbers you need. Must be within the [1,1e4] range.</param>
        /// <param name="mean">the distribution's mean. Must be within the [-1e6,1e6] range.</param>
        /// <param name="standardDeviation">the distribution's standard deviation. Must be within the [-1e6,1e6] range.</param>
        /// <param name="significantDigits">the number of significant digits to use. Must be within the [2,20] range.</param>
        /// <returns>double[] of true random doubles from a Gaussian distribution.</returns>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>double[] of true random doubles from a Gaussian distribution.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public double[] GenerateGaussians(int n, double mean, double standardDeviation, int significantDigits, JObject pregeneratedRandomization = null)
        {
            return this.ExtractDoubles(this.GaussianHelper(n, mean, standardDeviation, significantDigits,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return a list (size n) of true random unicode strings from the server. See 
        /// <a href="https://api.random.org/json-rpc/4/basic#generateStrings">here</a>.
        /// </summary>
        /// <param name="n">how many random strings you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">the length of each string. Must be within the [1,20] range. All strings will be of 
        /// the same length.</param>
        /// <param name="characters">a string that contains the set of characters that are allowed to occur in the 
        /// random strings. The maximum number of characters is 80.</param>
        /// <param name="replacement">specifies whether the random strings should be picked with replacement. If true, 
        /// the resulting list of strings may contain duplicates, otherwise the strings will all be unique (optional; default true).</param>
        /// <returns>string[] of true random strings.</returns>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>string[] of true random strings.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public string[] GenerateStrings(int n, int length, string characters, bool replacement = DefaultReplacement, JObject pregeneratedRandomization = null)
        {
            return this.ExtractStrings(this.StringHelper(n, length, characters, replacement,
                pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request and return a list (size n) of version 4 true random Universally Unique IDentifiers (UUIDs) in 
        /// accordance with section 4.4 of RFC 4122, from the server. See 
        /// <a href="https://api.random.org/json-rpc/4/basic#generateUUIDs">here</a>.
        /// </summary>
        /// <param name="n">how many random UUIDs you need. Must be within the [1,1e3] range.</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>Guid[] of true random UUIDs.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Guid[] GenerateUUIDs(int n, JObject pregeneratedRandomization = null)
        {
            return this.ExtractUUIDs(this.UUIDHelper(n, pregeneratedRandomization: pregeneratedRandomization,
                signed: false));
        }

        /// <summary>
        /// Request and return a list (size n) of Binary Large OBjects (BLOBs) as unicode strings containing true random 
        /// data from the server. See <a href="https://api.random.org/json-rpc/4/basic#generateBlobs">here</a>.
        /// </summary>
        /// <param name="n">how many random blobs you need. Must be within the [1,100] range.</param>
        /// <param name="size">the size of each blob, measured in bits. Must be within the [1,1048576] range and must be 
        /// divisible by 8.</param>
        /// <param name="format">specifies the format in which the blobs will be returned. Values allowed are <see cref="BlobFormatBase64"/> and 
        /// <see cref="BlobFormatHex"/> (optional; default BlobFormatBase64).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <returns>string[] of true random blobs as strings.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public string[] GenerateBlobs(int n, int size, string format = BlobFormatBase64, JObject pregeneratedRandomization = null)
        {
            return this.ExtractStrings(this.BlobHelper(n, size, format, pregeneratedRandomization: pregeneratedRandomization, signed: false));
        }

        /// <summary>
        /// Request a list (size n) of true random integers within a user-defined range from the server. Returns a dictionary 
        /// object with the parsed integer list mapped to 'data', the original response mapped to 'random', and the response's 
        /// signature mapped to 'signature'. See <a href="https://api.random.org/json-rpc/4/signed#generateSignedIntegers">here</a>.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within 
        /// the [-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers 
        /// may contain duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="integerBase">the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16 (optional;default 10).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random int[] if decimal (base 10) or random 
        /// string[] if non-decimal (any other base value)</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedIntegers(int n, int min, int max, bool replacement = true, int integerBase = 10, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.IntegerHelper(n, min, max, replacement, integerBase,
                pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>();
            if (integerBase == 10)
            {
                result.Add("data", this.ExtractInts(response));
            }
            else
            {
                result.Add("data", this.ExtractStrings(response));
            }
            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request and return uniform sequences of true random integers within user-defined ranges from the server. Returns a 
        /// dictionary object with the parsed 2D integer array mapped to 'data', the original response mapped to 'random', and the 
        /// response's signature mapped to 'signature'. See <a href="https://api.random.org/json-rpc/4/signed#generateIntegerSequences">here</a>.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">the length of each array of random integers requested. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within the 
        /// [-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the 
        /// [-1e9, 1e9] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting 
        /// numbers may contain duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="integerBase">the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16 (optional; default 10).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random int[][] if decimal (base 10) or random 
        /// string[][] if non-decimal (any other base value)</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedIntegerSequences(int n, int length, int min, int max, bool replacement = true, int integerBase = 10, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.IntegerSequenceHelper(n, length, min, max, replacement, integerBase,
                pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>();
            if (integerBase == 10)
            {
                result.Add("data", this.ExtractIntSequences(response));
            }
            else
            {
                result.Add("data", this.ExtractIntSequencesString(response));
            }
            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request and return uniform or multiform sequences of true random integers within user-defined ranges from the server. Returns a 
        /// dictionary object with the parsed 2D integer array mapped to 'data', the original response mapped to 'random', and the response's 
        /// signature mapped to 'signature'. See <a href="https://api.random.org/json-rpc/4/signed#generateIntegerSequences">here</a>.
        /// </summary>
        /// <param name="n">how many arrays of random integers you need. Must be within the [1,1e3] range.</param>
        /// <param name="length">an array with n integers each specifying the length of the sequence identified by its index. Each value 
        /// in the array must be within the[1, 1e4] range. </param>
        /// <param name="min">an array with n integers, each specifying the lower boundary of the sequence identified by its index. Each 
        /// value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="max">an array with n integers, each specifying the upper boundary of the sequence identified by its index. Each 
        /// value in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="replacement">an array with n Boolean values, each specifying whether the sequence identified by its index will be 
        /// created with or without replacement. If true, the resulting numbers may contain duplicate values, otherwise the numbers will 
        /// all be unique within each sequence (optional; default null, will be handled as an array of length n containing true).</param>
        /// <param name="integerBase">an array with n integer values, each specifying the base that will be used to display the sequence 
        /// identified by its index. Values allowed are 2, 8, 10 and 16 (optional; default null, will be handles as an array of lenght n 
        /// containg 10s). </param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random int[][] if decimal (all base 
        /// values are 10) or random string[][] if non-decimal (any other mix of base values)</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedIntegerSequences(int n, int[] length, int[] min, int[] max, bool[] replacement = null, int[] integerBase = null, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.IntegerSequenceHelper(n, length, min, max, replacement, integerBase,
                pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>();
            if (integerBase != null)
            {
                int[] defaultBase = FillArray(new int[n], DefaultIntBase);
                
                if (Enumerable.SequenceEqual(defaultBase, integerBase))
                {
                    result.Add("data", this.ExtractIntSequences(response));
                }
                else
                {
                    result.Add("data", this.ExtractIntSequencesString(response));
                }
            }

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request a list (size n) of true random decimal fractions, from a uniform distribution across the [0, 1] interval with a user-defined 
        /// number of decimal places from the server. Returns a dictionary object with the parsed decimal fraction list mapped to 'data', the original 
        /// response mapped to 'random', and the response's signature mapped to 'signature'. See 
        /// <a href="https://api.random.org/json-rpc/4/signed#generateSignedDecimalFractions">here</a>.
        /// </summary>
        /// <param name="n">how many random decimal fractions you need. Must be within the [1,1e4] range.</param>
        /// <param name="decimalPlaces">the number of decimal places to use. Must be within the [1,20] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers 
        /// may contain duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random double[]</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedDecimalFractions(int n, int decimalPlaces, bool replacement = true, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.DecimalFractionHelper(n, decimalPlaces, replacement, pregeneratedRandomization,
                licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "data", this.ExtractDoubles(response) }
            };

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request a list (size n) of true random numbers from a Gaussian distribution (also known as a normal distribution). The form uses 
        /// a Box-Muller Transform to generate the Gaussian distribution from uniformly distributed numbers. Returns a dictionary object with the 
        /// parsed random number list mapped to 'data', the original response mapped to 'random', and the response's signature mapped to 'signature'. 
        /// See <a href="https://api.random.org/json-rpc/4/signed#generateSignedGaussians">here</a>.
        /// </summary>
        /// <param name="n">how many random numbers you need. Must be within the [1,1e4] range.</param>
        /// <param name="mean">the distribution's mean. Must be within the [-1e6,1e6] range.</param>
        /// <param name="standardDeviation">the distribution's standard deviation. Must be within the [-1e6,1e6] range.</param>
        /// <param name="significantDigits">the number of significant digits to use. Must be within the [2,20] range.</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random double[]</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedGaussians(int n, double mean, double standardDeviation, int significantDigits, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.GaussianHelper(n, mean, standardDeviation, significantDigits,
                pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "data", this.ExtractDoubles(response) }
            };

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request a list (size n) of true random strings from the server. Returns a dictionary object with the parsed random string list mapped 
        /// to 'data', the original response mapped to 'random', and the response's signature mapped to 'signature'. See 
        /// <a href="https://api.random.org/json-rpc/4/signed#generateSignedStrings">here</a>.
        /// </summary>
        /// <param name="n">how many random strings you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">the length of each string. Must be within the [1,20] range. All strings will be of the same length.</param>
        /// <param name="characters">a string that contains the set of characters that are allowed to occur in the random strings. The maximum 
        /// number of characters is 80.</param>
        /// <param name="replacement">specifies whether the random strings should be picked with replacement. If true, the resulting list of 
        /// strings may contain duplicates, otherwise the strings will all be unique(optional; default true).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random string[]</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedStrings(int n, int length, string characters, bool replacement = true, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.StringHelper(n, length, characters, replacement, 
                pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "data", this.ExtractStrings(response) }
            };

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request a list (size n) of version 4 true random Universally Unique IDentifiers (UUIDs) in accordance with section 4.4 of RFC 4122, from 
        /// the server. Returns a dictionary object with the parsed random UUID list mapped to 'data', the original response mapped to 'random', and 
        /// the response's signature mapped to 'signature'. See <a href="https://api.random.org/json-rpc/4/signed#generateSignedUUIDs">here</a>.
        /// </summary>
        /// <param name="n">how many random UUIDs you need. Must be within the [1,1e3] range.</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random Guid[]</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedUUIDs(int n, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.UUIDHelper(n, pregeneratedRandomization, licenseData, userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "data", this.ExtractUUIDs(response) }
            };

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Request a list (size n) of Binary Large OBjects (BLOBs) containing true random data from the server. Returns a dictionary object with 
        /// the parsed random BLOB list mapped to 'data', the original response mapped to 'random', and the response's signature mapped to 'signature'. 
        /// See <a href="https://api.random.org/json-rpc/4/signed#generateSignedBlobs">here</a>.
        /// </summary>
        /// <param name="n">how many random blobs you need. Must be within the [1,100] range.</param>
        /// <param name="size">the size of each blob, measured in bits. Must be within the [1,1048576] range and must be divisible by 8.</param>
        /// <param name="format">specifies the format in which the blobs will be returned. Values allowed are <see cref="BlobFormatBase64"/> and 
        /// <see cref="BlobFormatHex"/> (optional; default BlobFormatBase64).</param>
        /// <param name="pregeneratedRandomization"> A JObject which allows the client to specify that the random values should
        /// be generated from a pregenerated, historical randomization instead of a one-time on-the-fly randomization. There are
        /// three possible cases:
        /// <para>- null: The standard way of calling for random values, i.e. true randomness is generated and
        /// discarded afterwards.</para>
        /// <para>- date: RANDOM.ORG uses historical true randomness generated on the corresponding date
        /// (past or present, format: { "date", "YYYY-MM-DD" }).</para>
        /// <para>- id: RANDOM.ORG uses historical true randomness derived from the corresponding identifier
        /// in a deterministic manner. Format: { "id", "PERSISTENT-IDENTIFIER" } where "PERSISTENT-IDENTIFIER" is
        /// a string with length in the [1, 64] range.</para>
        /// </param>
        /// <param name="licenseData">A JObject which allows the caller to include data of relevance to the license
        /// that is associated with the API Key. This is mandatory for API Keys with the license type "Flexible
        /// Gambling" and follows the format { "maxPayout": { "currency": "XTS", "amount": 0.0 }}. This information
        /// is used in licensing requested random values and in billing. The currently supported currencies are: "USD",
        /// "EUR", "GBP", "BTC". The most up-to-date information on the currencies can be found in the
        /// <a href="https://api.random.org/json-rpc/4/signed">Signed API documentation</a>.</param>
        /// <param name="userData">JObject that will be included in unmodified form. Its maximum size in encoded (string) form is 1,000 
        /// characters(optional; default null).</param>
        /// <param name="ticketId">A string with ticket identifier obtained via the <see cref="CreateTickets(int, bool)"/> method. Specifying 
        /// a value for ticketId will cause RANDOM.ORG to record that the ticket was used to generate the requested random values. Each ticket 
        /// can only be used once (optional; default null)</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string, "data": random string[]</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GenerateSignedBlobs(int n, int size, string format = BlobFormatBase64, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null)
        {
            JObject response = this.BlobHelper(n, size, format, pregeneratedRandomization, licenseData,
                userData, ticketId, signed: true);

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "data", this.ExtractStrings(response) }
            };

            return this.ExtractSignedResponse(response, result);
        }

        /// <summary>
        /// Retrieve signed random values generated within the last 24h, using a serial number. See 
        /// <a href="https://api.random.org/json-rpc/4/signed#getResult">here</a>.
        /// </summary>
        /// <param name="serialNumber">an integer containing the serial number associated with the response you wish to retrieve.</param>
        /// <returns>Dictionary with "random": random JObject, "signature": signature string</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GetResult(int serialNumber)
        {
            JObject request = new JObject
            {
                { "serialNumber", serialNumber }
            };

            request = this.GenerateKeyedRequest(request, GetResultMethod);

            JObject response = this.SendRequest(request);

            return this.ExtractSignedResponse(response, new Dictionary<string, object>());
        }

        /// <summary>
        /// Create n tickets to be used in signed value-generating methods. See <a href="https://api.random.org/json-rpc/4/signed#createTickets">here</a>.
        /// </summary>
        /// <param name="n">The number of tickets requested. This must be a number in the [1, 50] range.</param>
        /// <param name="showResult"> A bool value that determines how much information calls to <see cref="GetTicket(string)"/> will return. If showResult 
        /// is false, GetTicket() will return only the basic ticket information. If showResult is true, the full random and signature objects from the 
        /// response that was used to satisfy the ticket is returned.</param>
        /// <returns>JObject[] of ticket objects</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public JObject[] CreateTickets(int n, bool showResult)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "showResult", showResult }
            };

            request = this.GenerateKeyedRequest(request, CreateTicketMethod);

            JObject response = this.SendRequest(request);

            return this.ExtractTickets(response);
        }

        /// <summary>
        /// Obtain information about tickets linked with your API key. The maximum number of tickets that can be returned by this method is 2000. 
        /// See <a href="https://api.random.org/json-rpc/4/signed#listTickets">here</a>.
        /// </summary>
        /// <param name="ticketType">A string describing the type of tickets you want to obtain information about. Possible values are <c>singleton, head</c>
        /// and <c>tail</c>.
        /// <list type="bullet">
        /// <item>
        /// <term>singleton</term>
        /// <description>returns tickets that have no previous or next tickets.</description>
        /// </item>
        /// <item>
        /// <term>head</term>
        /// <description>returns tickets hat do not have a previous ticket but that do have a next ticket.</description>
        /// </item>
        /// <item>
        /// <term>tail</term>
        /// <description>returns tickets that have a previous ticket but do not have a next ticket. </description>
        /// </item>
        /// </list></param>
        /// <returns>JObject[] of tickets of the type requested</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public JObject[] ListTickets(string ticketType)
        {
            JObject request = new JObject
            {
                { "ticketType", ticketType }
            };

            request = this.GenerateKeyedRequest(request, ListTicketMethod);

            JObject response = this.SendRequest(request);

            return this.ExtractTickets(response);
        }

        /// <summary>
        /// Obtain information about a single ticket using the {@code ticketId} associated with it. If the ticket has <c>showResult</c> set to true 
        /// and has been used, this method will return the values generated. See <a href="https://api.random.org/json-rpc/4/signed#getTicket">here</a>.
        /// </summary>
        /// <param name="ticketId">A string containing a ticket identifier returned by a prior call to the <see cref="CreateTickets(int, bool)"/> method.</param>
        /// <returns>Dictionary with the following data: 
        /// <para/> If the ticket was created with <c>showResult true</c> and has been used in a signed value-generating method:
        /// <list type="bullet">
        /// <item>
        /// <term>"random"</term>
        /// <description>random JObject as returned from the server</description>
        /// </item>
        /// <item>
        /// <term>"signature"</term>
        /// <description>signature string</description>
        /// </item>
        /// <item>
        /// <term>"data"</term>
        /// <description>an array of random values of the type corresponding to the method that the ticket was used on</description>
        /// </item>
        /// </list>
        /// <para/> If the ticket was created with <c>showResult false</c> or has not yet been used:
        /// <list type="bullet">
        /// <item>
        /// <term>"result"</term>
        /// <description>JObject returned from the server</description>
        /// </item>
        /// </list>
        /// </returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public Dictionary<string, object> GetTicket(string ticketId)
        {
            JObject request = new JObject
            {
                { "ticketId", ticketId }
            };

            request = this.GenerateRequest(request, GetTicketMethod);

            JObject response = this.SendRequest(request);

            Dictionary<string, object> result = new Dictionary<string, object>();

            response = (JObject)response["result"];

            if (response.ContainsKey("result") && !response["result"].Equals(null))
            {
                string method = (string)((JObject)((JObject)response["result"])["random"])["method"];

                if (method.Equals(SignedIntegerMethod))
                {
                    if ((int)((JObject)((JObject)response["result"])["random"])["base"] == 10)
                    {
                        // decimal base
                        result.Add("data", this.ExtractInts(response));
                    }
                    else
                    {
                        // non-decimal base
                        result.Add("data", this.ExtractStrings(response));
                    }
                }
                else if (method.Equals(SignedIntegerSequenceMethod))
                {
                    bool decimalBase = false;
                    JObject random = (JObject)((JObject)response["result"])["random"];

                    if (random["base"] is JArray)
                    {
                        // Integer sequence method with array parameters
                        int[] defaultBase = FillArray(new int[(int)random["n"]], DefaultIntBase);

                        if (Enumerable.SequenceEqual(defaultBase, random["base"].Select(jv => (int)jv).ToArray()))
                        {
                            // Decimal base for all sequences requested
                            decimalBase = true;
                        }
                    }
                    else if ((int)random["base"] == 10)
                    {
                        // Integer sequence method with single value parameters and decimal base
                        decimalBase = true;
                    }
                    if (decimalBase)
                    {
                        result.Add("data", this.ExtractIntSequences(response));
                    }
                    else
                    {
                        result.Add("data", this.ExtractIntSequencesString(response));
                    }
                }
                else if (method.Equals(SignedDecimalFractionMethod) || method.Equals(SignedGaussianMethod))
                {
                    result.Add("data", this.ExtractDoubles(response));
                }
                else if (method.Equals(SignedStringMethod) || method.Equals(SignedBlobMethod))
                {
                    result.Add("data", this.ExtractStrings(response));
                }
                else if (method.Equals(SignedUUIDMethod))
                {
                    result.Add("data", this.ExtractUUIDs(response));
                }
                return this.ExtractSignedResponse(response, result);
            }
            else
            {
                // Returns the information for a ticket with showResult == false OR 
                // a ticket with showResult == true, but which has not yet been used
                result.Add("result", response);
                return result;
            }
        }

        /// <summary>
        /// *Simplified Version* Verify the signature of a response previously received from one of the methods in he Signed API with the server. 
        /// This is used to examine the authenticity of numbers. Returns true on verification success. See 
        /// <a href="https://api.random.org/json-rpc/4/signed#verifySignature">here</a>.
        /// </summary>
        /// <param name="random">Dictionary object as it is returned by RANDOM.ORG through one of the Signed API methods. </param>
        /// <returns>verification success.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public bool VerifySignature(Dictionary<string, object> random)
        {
            return VerifySignature((JObject)random["random"], (string)random["signature"]);
        }

        /// <summary>
        /// Verify the signature of a response previously received from one of the methods in he Signed API with the server. 
        /// This is used to examine the authenticity of numbers. Returns true on verification success. See 
        /// <a href="https://api.random.org/json-rpc/4/signed#verifySignature">here</a>.
        /// </summary>
        /// <param name="random">the random field from a response returned by RANDOM.ORG through one of the Signed API methods.</param>
        /// <param name="signature">the signature field from the same response that the random field originates from.</param>
        /// <returns>verification success.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public bool VerifySignature(JObject random, string signature)
        {
            JObject request = new JObject
            {
                { "random", random },
                { "signature", signature }
            };

            request = this.GenerateRequest(request, VerifySignatureMethod);

            JObject response = this.SendRequest(request);

            return this.ExtractVerificationResponse(response);
        }

        /// <summary>
        /// Create the URL for the signature verification page of a signed response.
        /// </summary>
        /// <remarks>The web-page accessible from this URL will contain the details of the response used in this method,
        /// provided that the signature can be verified. This URL is also shown under "Show Technical Details" when the
        /// online <a href="https://api.random.org/signatures/form">Signature Verification Form</a> is used to validate
        /// a signature.
        /// <para>Note: this method throws a RandomOrgRANDOMORGException if the length of the URL created
        /// exceeds the maximum length permitted (2,046 characters).</para></remarks>
        /// <param name="random">the random field from a response returned by RANDOM.ORG through one of the Signed API methods.</param>
        /// <param name="signature">the signature field from the same response that the random field originates from.</param>
        /// <returns>string containing the signature verification URL</returns>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the URL is too long (max. 2,046 characters)</exception>
        public string CreateUrl(JObject random, string signature)
        {
            string randomString = UrlFormatting(JsonConvert.SerializeObject(random));
            signature = UrlFormatting(signature);

            string url = "https://api.random.org/signatures/form?format=json";
            url += "&random=" + randomString;
            url += "&signature=" + signature;
            
            // throw an error if the maximum length allowed (2,046 characters) is exceeded
            if (url.Length > MaximumLengthUrl)
            {
                throw new RandomOrgRANDOMORGException("Error: URL exceeds maximum length (" + MaximumLengthUrl 
                    + " characters).");
            }

            return url;
        }

        /// <summary>
        /// Create the HTML form for the signature verification page of a signed response.
        /// </summary>
        /// <remarks>
        /// The web-page accessible from the "Validate" button created will contain the details of the
        /// response used in this method, provided that the signature can be verified. The same HTML form
        /// is also shown under "Show Technical Details" when the online
        /// <a href="https://api.random.org/signatures/form">Signature Verification Form</a> is used to
        /// validate a signature.
        /// </remarks>
        /// <param name="random">the random field from a response returned by RANDOM.ORG through one of the Signed API methods.</param>
        /// <param name="signature">the signature field from the same response that the random field originates from.</param>
        /// <returns>string containing the code for the HTML form</returns>
        public string CreateHtml(JObject random, string signature)
        {
            string randomString = JsonConvert.SerializeObject(random);

            string html = "<form action='https://api.random.org/signatures/form\' method='post'>\n";
            html += "  " + HtmlInput("hidden", "format", "json") + "\n";
            html += "  " + HtmlInput("hidden", "random", randomString) + "\n";
            html += "  " + HtmlInput("hidden", "signature", signature) + "\n";
            html += "  <input type='submit' value='Validate' />\n</form>";

            return html;
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integers. The RandomOrgCache can be polled for new results conforming to the output format of the input 
        /// request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may contain 
        /// duplicate values, otherwise the numbers will all be unique(optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 20, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;int[]&gt;</returns>
        public RandomOrgCache<int[]> CreateIntegerCache(int n, int min, int max, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSize)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "min", min },
                { "max", max },
                { "replacement", replacement }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(max - min + 1) / Math.Log(2) * n);

            return new RandomOrgCache<int[]>(this.SendRequest, this.ExtractInts, request, cacheSize, bulkN,
                n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integers. The RandomOrgCache can be polled for new results conforming to the output format of the input 
        /// request. RandomOrgCache type is same as expected return value. Note: This method creates a RandomOrgCache with <strong>string</strong> arrays, 
        /// as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="integerBase">the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16. </param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may contain 
        /// duplicate values, otherwise the numbers will all be unique(optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 20, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;string[]&gt;</returns>
        public RandomOrgCache<string[]> CreateIntegerCache(int n, int min, int max, int integerBase, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSize)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "min", min },
                { "max", max },
                { "replacement", replacement },
                { "base", integerBase }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(max - min + 1) / Math.Log(2) * n);

            return new RandomOrgCache<string[]>(this.SendRequest, this.ExtractStrings, request, cacheSize, bulkN,
                n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integer sequences. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">the length of each array of random integers requested. Must be within the[1, 1e4] range. </param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may contain 
        /// duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time(default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;int[][]&gt;</returns>
        public RandomOrgCache<int[][]> CreateIntegerSequenceCache(int n, int length, int min, int max, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "length", length },
                { "min", min },
                { "max", max },
                { "replacement", replacement }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(max - min + 1) / Math.Log(2) * n);

            return new RandomOrgCache<int[][]>(this.SendRequest, this.ExtractIntSequences, request, cacheSize, bulkN,
                n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integer sequences. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value. Note: This method creates a RandomOrgCache with 
        /// <strong>string</strong> arrays, as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">the length of each array of random integers requested. Must be within the[1, 1e4] range. </param>
        /// <param name="min">the lower boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="max">the upper boundary for the range from which the random numbers will be picked. Must be within the[-1e9, 1e9] range.</param>
        /// <param name="integerBase">the base that will be used to display the numbers. Values allowed are 2, 8, 10 and 16 (default 10).</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may contain 
        /// duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time(default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;string[][]&gt;</returns>
        public RandomOrgCache<string[][]> CreateIntegerSequenceCache(int n, int length, int min, int max, int integerBase, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "length", length },
                { "min", min },
                { "max", max },
                { "replacement", replacement },
                { "base", integerBase }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(max - min + 1) / Math.Log(2) * n);

            return new RandomOrgCache<string[][]>(this.SendRequest, this.ExtractIntSequencesString, request, cacheSize, bulkN,
                n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integer sequences. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">an array with n integers each specifying the length of the sequence identified by its index.Each value in the 
        /// array must be within the [1,1e4] range.</param>
        /// <param name="min">an array with n integers, each specifying the lower boundary of the sequence identified by its index. Each value 
        /// in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="max">an array with n integers, each specifying the upper boundary of the sequence identified by its index. Each value 
        /// in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="replacement">an array with n Boolean values, each specifying whether the sequence identified 
        /// by its index will be created with or without replacement. If true, the resulting numbers may contain duplicate values, 
        /// otherwise the numbers will all be unique within each sequence (optional; default null, will be handled as an array of length 
        /// n containing true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;int[][]&gt;</returns>
        public RandomOrgCache<int[][]> CreateIntegerSequenceCache(int n, int[] length, int[] min, int[] max, bool[] replacement = null, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            bool[] defaultReplacement = FillArray(new bool[n], true);

            if (replacement == null)
            {
                replacement = defaultReplacement;
            }

            JObject request = new JObject();

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size 
            // if requests can't be fulfilled.
            if (Enumerable.SequenceEqual(replacement, defaultReplacement))
            {
                bulkN = cacheSize / 2;

                request.Add("n", bulkN * n);

                length = Adjust(length, bulkN * n);
                min = Adjust(min, bulkN * n);
                max = Adjust(max, bulkN * n);
                replacement = Adjust(replacement, bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            request.Add("length", JArray.FromObject(length));
            request.Add("min", JArray.FromObject(min));
            request.Add("max", JArray.FromObject(max));
            request.Add("replacement", JArray.FromObject(replacement));

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);

            // max single request size, in bits, for adjusting bulk requests later


            int maxRequestSize = (int)Math.Ceiling(Math.Log(max.Max() - min.Min() + 1) / Math.Log(2) * n * length.Max());

            return new RandomOrgCache<int[][]>(this.SendRequest, this.ExtractIntSequences, request, cacheSize, bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random integer sequences. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value. Note: This method creates a RandomOrgCache with 
        /// <strong>string</strong> arrays, as it also handles requests for non-decimal integers.
        /// </summary>
        /// <param name="n">how many random integers you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">an array with n integers each specifying the length of the sequence identified by its index. Each value in the 
        /// array must be within the [1,1e4] range.</param>
        /// <param name="min">an array with n integers, each specifying the lower boundary of the sequence identified by its index. Each value 
        /// in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="max">an array with n integers, each specifying the upper boundary of the sequence identified by its index. Each value 
        /// in the array must be within the [-1e9,1e9] range.</param>
        /// <param name="integerBase">an array with n integer values, each specifying the base that will be used to display the sequence identified 
        /// by its index. Values allowed are 2, 8, 10 and 16 (default 10).</param>
        /// <param name="replacement">an array with n Boolean values, each specifying whether the sequence identified 
        /// by its index will be created with or without replacement. If true, the resulting numbers may contain duplicate values, 
        /// otherwise the numbers will all be unique within each sequence (optional; default null, will be handled as an array of length 
        /// n containing true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;string[][]&gt;</returns>
        public RandomOrgCache<string[][]> CreateIntegerSequenceCache(int n, int[] length, int[] min, int[] max, int[] integerBase, bool[] replacement = null, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            bool[] defaultReplacement = FillArray(new bool[n], true);

            if (replacement == null)
            {
                replacement = defaultReplacement;
            }

            JObject request = new JObject();

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size 
            // if requests can't be fulfilled.
            if (Enumerable.SequenceEqual(replacement, defaultReplacement))
            {
                bulkN = cacheSize / 2;

                request.Add("n", bulkN * n);

                length = Adjust(length, bulkN * n);
                min = Adjust(min, bulkN * n);
                max = Adjust(max, bulkN * n);
                replacement = Adjust(replacement, bulkN * n);
                integerBase = Adjust(integerBase, bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            request.Add("length", JArray.FromObject(length));
            request.Add("min", JArray.FromObject(min));
            request.Add("max", JArray.FromObject(max));
            request.Add("replacement", JArray.FromObject(replacement));
            request.Add("base", JArray.FromObject(integerBase));

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);

            // max single request size, in bits, for adjusting bulk requests later


            int maxRequestSize = (int)Math.Ceiling(Math.Log(max.Max() - min.Min() + 1) / Math.Log(2) * n * length.Max());

            return new RandomOrgCache<String[][]>(this.SendRequest, this.ExtractIntSequencesString, request, cacheSize, bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random decimal fractions. The RandomOrgCache can be polled for new results conforming to the output format 
        /// of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random decimal fractions you need. Must be within the [1,1e4] range.</param>
        /// <param name="decimalPlaces">the number of decimal places to use. Must be within the [1,20] range.</param>
        /// <param name="replacement">specifies whether the random numbers should be picked with replacement. If true, the resulting numbers may 
        /// contain duplicate values, otherwise the numbers will all be unique (optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 20, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;double[]&gt;</returns>
        public RandomOrgCache<double[]> CreateDecimalFractionCache(int n, int decimalPlaces, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSize)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "decimalPlaces", decimalPlaces },
                { "replacement", replacement }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size 
            // if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, DecimalFractionMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(10) / Math.Log(2) * decimalPlaces * n);

            return new RandomOrgCache<double[]>(this.SendRequest, this.ExtractDoubles, request, cacheSize,
                bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random numbers from a Gaussian distribution. The RandomOrgCache can be polled for new results conforming 
        /// to the output format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random numbers you need. Must be within the [1,1e4] range.</param>
        /// <param name="mean">the distribution's mean. Must be within the [-1e6,1e6] range.</param>
        /// <param name="standardDeviation">the distribution's standard deviation. Must be within the [-1e6,1e6] range.</param>
        /// <param name="significantDigits">the number of significant digits to use. Must be within the [2,20] range.</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 20, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;double[]&gt;</returns>
        public RandomOrgCache<double[]> CreateGaussianCache(int n, double mean, double standardDeviation, int significantDigits, int cacheSize = DefaultCacheSize)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "mean", mean },
                { "standardDeviation", standardDeviation },
                { "significantDigits", significantDigits }
            };

            int bulkN = 0;

            // make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if 
            // requests can't be fulfilled.
            bulkN = cacheSize / 2;
            request.Add("n", bulkN * n);

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, GaussianMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(Math.Pow(10, significantDigits)) / Math.Log(2) * n);

            return new RandomOrgCache<double[]>(this.SendRequest, this.ExtractDoubles, request, cacheSize,
                bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random strings. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random strings you need. Must be within the [1,1e4] range.</param>
        /// <param name="length">the length of each string. Must be within the [1,20] range. All strings will be of the same length.</param>
        /// <param name="characters">a string that contains the set of characters that are allowed to occur in the random strings. 
        /// The maximum number of characters is 80.</param>
        /// <param name="replacement">specifies whether the random strings should be picked with replacement. If true, the resulting 
        /// list of strings may contain duplicates, otherwise the strings will all be unique(optional; default true).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 20, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;string[]&gt;</returns>
        public RandomOrgCache<string[]> CreateStringCache(int n, int length, string characters, bool replacement = DefaultReplacement, int cacheSize = DefaultCacheSize)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "length", length },
                { "characters", characters },
                { "replacement", replacement }
            };

            int bulkN = 0;

            // If possible, make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size 
            // if requests can't be fulfilled.
            if (replacement)
            {
                bulkN = cacheSize / 2;
                request.Add("n", bulkN * n);

                // not possible to make the request more efficient
            }
            else
            {
                request.Add("n", n);
            }

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, StringMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = (int)Math.Ceiling(Math.Log(characters.Length) / Math.Log(2) * length * n);

            return new RandomOrgCache<string[]>(this.SendRequest, this.ExtractStrings, request, cacheSize,
                bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain UUIDs. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random UUIDs you need. Must be within the [1,1e3] range.</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;Guid[]&gt;</returns>
        public RandomOrgCache<Guid[]> CreateUUIDCache(int n, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject();

            int bulkN = 0;

            // make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size if 
            // requests can't be fulfilled.
            bulkN = cacheSize / 2;
            request.Add("n", bulkN * n);

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, UUIDMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = n * UUIDSize;

            return new RandomOrgCache<Guid[]>(this.SendRequest, this.ExtractUUIDs, request, cacheSize,
                bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Get a RandomOrgCache to obtain random blobs. The RandomOrgCache can be polled for new results conforming to the output 
        /// format of the input request. RandomOrgCache type is same as expected return value.
        /// </summary>
        /// <param name="n">how many random blobs you need. {@code n*(cacheSize/2)} must be within the [1,100] range.</param>
        /// <param name="size">the size of each blob, measured in bits. Must be within the [1,1048576] range and must be divisible by 8.</param>
        /// <param name="format">specifies the format in which the blobs will be returned. Values allowed are <see cref="BlobFormatBase64"/> and 
        /// <see cref="BlobFormatHex"/> (optional; default BlobFormatBase64).</param>
        /// <param name="cacheSize">number of result-sets for the cache to try to maintain at any given time (optional; default 10, minimum 2).</param>
        /// <returns>RandomOrgCache&lt;string[]&gt;</returns>
        public RandomOrgCache<string[]> CreateBlobCache(int n, int size, string format = BlobFormatBase64, int cacheSize = DefaultCacheSizeSmall)
        {
            if (cacheSize < 2)
            {
                cacheSize = 2;
            }

            JObject request = new JObject
            {
                { "size", size },
                { "format", format }
            };

            int bulkN = 0;

            // make requests more efficient by bulk-ordering from the server. 
            // initially set at cache_size/2, but cache will auto-shrink bulk request size 
            // if requests can't be fulfilled.
            bulkN = cacheSize / 2;
            request.Add("n", bulkN * n);

            // get the request object for use in all requests from this cache
            request = this.GenerateKeyedRequest(request, BlobMethod);

            // max single request size, in bits, for adjusting bulk requests later
            int maxRequestSize = n * size;

            return new RandomOrgCache<string[]>(this.SendRequest, this.ExtractStrings, request, cacheSize,
                bulkN, n, maxRequestSize);
        }

        /// <summary>
        /// Return the (estimated) number of remaining API requests available to the client. If cached usage info is older than 
        /// <see cref="AllowanceStateRefreshSeconds"/> fresh info is obtained from the server. If fresh info has to be obtained the following 
        /// exceptions can be raised.
        /// </summary>
        /// <returns>number of requests remaining.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public int GetRequestsLeft()
        {
            if (this.requestsLeft < 0 || this.CurrentTimeMillis() > (this.lastResponseReceivedTime + AllowanceStateRefreshSeconds))
            {
                this.GetUsage();
            }
            return this.requestsLeft;
        }

        /// <summary>
        /// Return the (estimated) number of remaining true random bits available to the client. If cached usage info is older than 
        /// <see cref="AllowanceStateRefreshSeconds"/> fresh info is obtained from the server. If fresh info has to be obtained the following 
        /// exceptions can be raised.
        /// </summary>
        /// <returns>number of bits remaining.</returns>
        /// <exception cref="RandomOrgSendTimeoutException">Thrown when blocking timeout is exceeded before the request can be sent. </exception>
        /// <exception cref="RandomOrgKeyNotRunningException">Thrown when the API key has been stopped. </exception>
        /// <exception cref="RandomOrgInsufficientRequestsException">Thrown when the API key's server requests allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgInsufficientBitsException">Thrown when the API key's server bits allowance has been exceeded.</exception>
        /// <exception cref="RandomOrgBadHTTPResponseException">Thrown when a HTTP 200 OK response is not received.</exception>
        /// <exception cref="RandomOrgRANDOMORGException">Thrown when the server returns a RANDOM.ORG Error.</exception>
        /// <exception cref="RandomOrgJSONRPCException">Thrown when the server returns a JSON-RPC Error.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs.</exception>
        public int GetBitsLeft()
        {
            if (this.bitsLeft < 0 || this.CurrentTimeMillis() > (this.lastResponseReceivedTime + AllowanceStateRefreshSeconds))
            {
                this.GetUsage();
            }
            return this.bitsLeft;
        }

        /// <summary>
        /// Issue a getUsage request to update bits and requests left.
        /// </summary>
        private void GetUsage()
        {
            JObject request = new JObject();

            request = this.GenerateKeyedRequest(request, GetUsageMethod);
            
            this.SendRequest(request);
        }

        /// <summary>
        /// Add generic request parameters and API key to custom request.
        /// </summary>
        /// <param name="parameters">custom parameters to generate request around.</param>
        /// <param name="method">method to send request to.</param>
        /// <returns>fleshed out JSON request</returns>
        private JObject GenerateKeyedRequest(JObject parameters, string method)
        {
            parameters.Add("apiKey", this.apiKey);

            return this.GenerateRequest(parameters, method);
        }

        /// <summary>
        /// Add generic request parameters to custom request.
        /// </summary>
        /// <param name="parameters">custom parameters to generate request around.</param>
        /// <param name="method">method to send request to.</param>
        /// <returns>fleshed out JSON request</returns>
        private JObject GenerateRequest(JObject parameters, string method)
        {
            JObject request = new JObject
            {
                { "jsonrpc", "2.0" },
                { "method", method },
                { "params", parameters },
                { "id", Guid.NewGuid().ToString() }
            };

            return request;
        }

        /// <summary>
        /// Extracts int[] from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted int[]</returns>
        protected int[] ExtractInts(JObject response)
        {
            return this.ExtractResponse(response).Select(jv => (int)jv).ToArray();
        }

        /// <summary>
        /// Extracts int[][] from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted int[][]</returns>
        protected int[][] ExtractIntSequences(JObject response)
        {
            JArray data = this.ExtractResponse(response);
            int[][] randoms = new int[data.Count][];

            for (int i = 0; i < randoms.Length; i++)
            {
                randoms[i] = ((JArray)data[i]).Select(jv => (int)jv).ToArray();
            }

            return randoms;
        }

        /// <summary>
        /// Extracts String[][] from JSON response.
        /// </summary>
        /// <param name="response">response JSON from which to extract data.</param>
        /// <returns>extracted String[][]</returns>
        protected string[][] ExtractIntSequencesString(JObject response)
        {
            JArray data = this.ExtractResponse(response);
            string[][] randoms = new string[data.Count][];

            for (int i = 0; i < randoms.Length; i++)
            {
                randoms[i] = ((JArray)data[i]).Select(jv => (string)jv).ToArray();
            }

            return randoms;
        }

        /// <summary>
        /// Extracts double[] from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted double[]</returns>
        protected double[] ExtractDoubles(JObject response)
        {
            return this.ExtractResponse(response).Select(jv => (double)jv).ToArray();
        }

        /// <summary>
        /// Extracts string[] from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted string[]</returns>
        protected string[] ExtractStrings(JObject response)
        {
            return this.ExtractResponse(response).Select(jv => (string)jv).ToArray();
        }

        /// <summary>
        /// Extracts Guid[] from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted Guid[]</returns>
        protected Guid[] ExtractUUIDs(JObject response)
        {
            return this.ExtractResponse(response).Select(jv => (Guid)jv).ToArray();
        }

        /// <summary>
        /// Extracts JObject[] of tickets from JSON response.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>extracted JObject[]</returns>
        protected JObject[] ExtractTickets(JObject response)
        {
            JArray t = (JArray)response["result"];
            JObject[] tickets = new JObject[t.Count];
            for (int i = 0; i < tickets.Length; i++)
            {
                tickets[i] = (JObject)t[i];
            }
            return tickets;
        }

        /// <summary>
        /// Gets random data as separate from response JSON.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <returns>JArray of random data</returns>
        private JArray ExtractResponse(JObject response)
        {
            return (JArray)((JObject)((JObject)response["result"])["random"])["data"];
        }

        /// <summary>
        /// Gets signing data from response JSON and add to result Dictionary.
        /// </summary>
        /// <param name="response">JSON from which to extract data.</param>
        /// <param name="result">result to add signing data to.</param>
        /// <returns>the passed in result Dictionary</returns>
        private Dictionary<string, object> ExtractSignedResponse(JObject response, Dictionary<string, object> result)
        {
            result.Add("random", (JObject)((JObject)response["result"])["random"]);
            result.Add("signature", (string)((JObject)response["result"])["signature"]);

            return result;
        }

        /// <summary>
        /// Gets verification response as separate from response JSON.
        /// </summary>
        /// <param name="response">JSON from which to extract verification response.</param>
        /// <returns>verification success</returns>
        private bool ExtractVerificationResponse(JObject response)
        {
            return (bool)((JObject)response["result"])["authenticity"];
        }

        /// <summary>
        /// Send request as determined by serialized boolean.
        /// </summary>
        /// <param name="request">JSON to send.</param>
        /// <returns>JObject response</returns>
        protected JObject SendRequest(JObject request)
        {

            return this.serialized ? this.SendSerializedRequest(request) : this.SendUnserializedRequest(request);
        }

        /// <summary>
        /// Immediate call to server.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private JObject SendUnserializedRequest(JObject request)
        {
            // Send request immediately.
            UnserializedRunnable r = new UnserializedRunnable(request, this);
            r.Run();
            
            // Wait for response to arrive.
            while (r.GetData() == null)
            {
                try
                {
                    Thread.Sleep(50);
                }
                catch (ThreadInterruptedException)
                {
                    System.Diagnostics.Debug.WriteLine("Client interrupted while waiting for server to "
                            + "return a response.");
                }
            }

            // Raise any thrown exceptions.
            if (r.GetData().ContainsKey("exception"))
            {
                this.ThrowException((Exception)r.GetData()["exception"]);
            }

            // Return response.
            return (JObject)r.GetData()["response"];
        }

        /// <summary>
        /// Runnable for unserialized network calls. 
        /// </summary>
        class UnserializedRunnable
        {
            private readonly JObject request;
            private readonly RandomOrgClient client;
            private Dictionary<string, object> data;

            public UnserializedRunnable(JObject request, RandomOrgClient client)
            {
                this.request = request;
                this.client = client;
            }

            public void Run()
            {
                this.data = this.client.SendRequestCore(this.request);
            }

            /// <returns>data returned by network request - or null if not yet arrived.</returns>
            public Dictionary<string, object> GetData()
            {
                return this.data;
            }
        }

        /// <summary>
        /// Add request to queue to be executed by networking thread one-by-one. Method blocks until this request receives a response or times out.
        /// </summary>
        /// <param name="request">JSON to send.</param>
        /// <returns>JObject response</returns>
        private JObject SendSerializedRequest(JObject request)
        {
            // Creating request to add to the queue with it's own lock.
            object requestLock = new object();

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                { "lock", requestLock },
                { "request", request },
                { "response", null },
                { "exception", null }
            };

            // Wait on the lock for the specified blocking timeout.
            lock (requestLock)
            {

                // Adding request to the queue
                lock (this.serializedQueue)
                {
                    this.serializedQueue.Enqueue(data);
                    Monitor.Pulse(serializedQueue);
                }

                try
                {
                    if (this.blockingTimeout == -1)
                    {
                        Monitor.Wait(requestLock);
                    }
                    else
                    {
                        Monitor.Wait(requestLock, (int)this.blockingTimeout);
                    }
                }
                catch (ThreadInterruptedException)
                {
                    System.Diagnostics.Debug.WriteLine("Client interrupted while waiting for request to be sent.");
                }

                // Lock has now either been notified or timed out. Examine data to determine 
                // which and react accordingly.

                // Request wasn't sent in time, cancel and raise exception.
                if (data["response"] == null && data["exception"] == null)
                {
                    data["request"] = null;
                    throw new RandomOrgSendTimeoutException("The maximum allowed blocking time of " + this.blockingTimeout 
                        + "millis has been exceeded while waiting for a synchronous request to send.");
                }

                // Exception on sending request.
                if (data["exception"] != null)
                {
                    this.ThrowException((Exception)data["exception"]);
                }

                // Request was successful.
                return (JObject)data["response"];
            }
        }

        /// <summary>
        /// Thread to synchronously send requests in queue. 
        /// </summary>
        protected void ThreadedRequestSending()
        {
            // Thread to execute queued requests.
            while (true)
            {

                Dictionary<string, object> request;
                lock (this.serializedQueue)
                {
                    // Block and wait for a request.
                    if (this.serializedQueue.Count == 0)
                    {
                        try
                        {
                            Monitor.Wait(this.serializedQueue);
                        }
                        catch (ThreadInterruptedException)
                        {
                            System.Diagnostics.Debug.WriteLine("Client thread interrupted while waiting "
                                    + "for a request to send.");
                        }
                    }

                    request = this.serializedQueue.Dequeue();
                }


                // Get the request's lock to indicate request in progress.
                lock (request["lock"])
                {

                    // If request still exists it hasn't been cancelled.
                    if (request["request"] != null)
                    {

                        // Send request.
                        Dictionary<string, object> data = this.SendRequestCore((JObject)request["request"]);

                        // Set result.
                        if (data.ContainsKey("exception"))
                        {
                            request["exception"] = data["exception"];
                        }
                        else
                        {
                            request["response"] = data["response"];
                        }
                    }

                    // Notify completion and return
                    Monitor.Pulse(request["lock"]);
                }
            }
        }

        /// <summary>
        /// Throw specific Exception types.
        /// </summary>
        /// <param name="e">exception to throw.</param>
        private void ThrowException(Exception e)
        {
            if (e is RandomOrgSendTimeoutException)
            {
                throw (RandomOrgSendTimeoutException)e;
            }
            else if (e is RandomOrgKeyNotRunningException)
            {
                throw (RandomOrgKeyNotRunningException)e;
            }
            else if (e is RandomOrgInsufficientRequestsException)
            {
                throw (RandomOrgInsufficientRequestsException)e;
            }
            else if (e is RandomOrgInsufficientBitsException)
            {
                throw (RandomOrgInsufficientBitsException)e;
            }
            else if (e is RandomOrgBadHTTPResponseException)
            {
                throw (RandomOrgBadHTTPResponseException)e;
            }
            else if (e is RandomOrgRANDOMORGException)
            {
                throw (RandomOrgRANDOMORGException)e;
            }
            else if (e is RandomOrgJSONRPCException)
            {
                throw (RandomOrgJSONRPCException)e;
            }
            else if (e is IOException)
            {
                throw (IOException)e;
            }
        }

        /// <summary>
        /// Core send request function.
        /// </summary>
        /// <param name="request">JSON to send.</param>
        /// <returns>info on request success/response in a HashMap with one or other of the following entries: "exception" - 
        /// an exception which may be one of those found in <see cref="RandomOrg.CoreApi.Errors"/>; "response" - JObject response</returns>
        protected Dictionary<string, object> SendRequestCore(JObject request)
        {

            Dictionary<string, object> ret = new Dictionary<string, object>();

            // If a back-off is set, no more requests can be issued until the required 
            // back-off time is up.
            if (this.backoff != -1)
            {

                // Time not yet up, throw exception.
                if (this.CurrentTimeMillis() < this.backoff)
                {
                    ret.Add("exception", new RandomOrgInsufficientRequestsException(this.backoffError));
                    return ret;
                    // Time is up, clear back-off.
                }
                else
                {
                    this.backoff = -1;
                    this.backoffError = null;
                }
            }

            long wait;

            // Check server advisory delay.
            lock (this.advisoryDelayLock)
            {
                wait = this.advisoryDelay - (this.CurrentTimeMillis() - this.lastResponseReceivedTime);
            }

            // Wait the specified delay if necessary and if wait time is not longer than the 
            // set blocking timeout.
            if (wait > 0)
            {
                if (this.blockingTimeout != -1 && wait > this.blockingTimeout)
                {
                    ret.Add("exception", new RandomOrgSendTimeoutException("The server advisory delay of " + wait 
                        + "millis is greater than the defined maximum allowed blocking time of " + this.blockingTimeout 
                        + "millis."));
                    return ret;
                }
                try
                {
                    Thread.Sleep((int)wait);
                }
                catch (ThreadInterruptedException)
                {
                    System.Diagnostics.Debug.WriteLine("Client interrupted while waiting for server "
                            + "mandated blocking time.");
                }
            }

            JObject response;

            // Send the request
            try
            {
                response = this.Post(request);
            }
            catch (RandomOrgBadHTTPResponseException e)
            {
                ret.Add("exception", e);
                return ret;
            }
            catch (IOException e)
            {
                ret.Add("exception", e);
                return ret;
            }

            // Parse the response.

            // Has error?
            if (response.ContainsKey("error"))
            {
                JObject error = (JObject)response["error"];

                int code = (int)error["code"];
                string message = (string)error["message"];

                // RandomOrgAllowanceExceededError, API key not running, backoff until midnight UTC, 
                // from RANDOM.ORG Errors: https://api.random.org/json-rpc/4/error-codes
                if (code == 402)
                {
                    this.backoff = this.UtcMidnightMillis();
                    this.backoffError = "Error " + code + ": " + message;
                    ret.Add("exception", new RandomOrgInsufficientRequestsException(this.backoffError));
                    return ret;
                }
                else if (code == 401)
                {
                    ret.Add("exception", new RandomOrgKeyNotRunningException("Error " + code
                            + ": " + message));
                    return ret;

                }
                else if (code == 403)
                {
                    ret.Add("exception", new RandomOrgInsufficientBitsException("Error " + code
                            + ": " + message, this.bitsLeft));
                    return ret;

                    // RandomOrgRANDOMORGError from RANDOM.ORG Errors: 
                    // https://api.random.org/json-rpc/4/error-codes
                }
                else if (RandomOrgErrors.Contains(code))
                {
                    ret.Add("exception", new RandomOrgRANDOMORGException("Error " + code
                            + ": " + message, code));
                    return ret;

                    // RandomOrgJSONRPCError from JSON-RPC Errors: 
                    // https://api.random.org/json-rpc/4/error-codes
                }
                else
                {
                    ret.Add("exception", new RandomOrgJSONRPCException("Error " + code
                            + ": " + message));
                    return ret;
                }
            }

            string method = (string)request["method"];

            if (method.Equals("listTickets") || method.Equals("createTickets") || method.Equals("getTicket"))
            {
                // Set default server advisory delay
                lock (this.advisoryDelayLock)
                {
                    this.advisoryDelay = DefaultDelay;
                    this.lastResponseReceivedTime = this.CurrentTimeMillis();
                }
            }
            else
            {
                JObject result = (JObject)response["result"];

                // Update usage statistics
                if (result.ContainsKey("requestsLeft"))
                {
                    this.requestsLeft = (int)result["requestsLeft"];
                    this.bitsLeft = (int)result["bitsLeft"];
                }

                // Set new server advisory delay
                lock (this.advisoryDelayLock)
                {
                    if (result.ContainsKey("advisoryDelay"))
                    {
                        this.advisoryDelay = (int)result["advisoryDelay"];
                    }
                    else
                    {
                        // Use default if none from server.
                        this.advisoryDelay = DefaultDelay;
                    }

                    this.lastResponseReceivedTime = this.CurrentTimeMillis();
                }
            }

            ret.Add("response", response);
            return ret;
        }

        /// <summary>
        /// POST JSON to server and return JSON response.
        /// </summary>
        /// <param name="json">request to post. </param>
        /// <returns>JSON response.</returns>
        private JObject Post(JObject json)
        {
            HttpWebRequest con = (HttpWebRequest)WebRequest.Create("https://api.random.org/json-rpc/4/invoke");
            con.Timeout = this.httpTimeout;

            // headers		
            con.Method = "POST";
            con.ContentType = "application/json";

            using (var streamWriter = new StreamWriter(con.GetRequestStream()))
            {
                streamWriter.Write(json.ToString());
            }

            var response = (HttpWebResponse)con.GetResponse();
            HttpStatusCode responseCode = response.StatusCode;

            if (responseCode.Equals(HttpStatusCode.OK))
            {
                using var streamReader = new StreamReader(response.GetResponseStream());
                return JObject.Parse(streamReader.ReadToEnd());
            }
            else
            {
                throw new RandomOrgBadHTTPResponseException("Error " + responseCode.ToString());
            }
        }

        /// <summary>
        /// Helper function to generate and send requests for integer methods.
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject IntegerHelper(int n, int min, int max, bool replacement = true, int integerBase = 10, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "min", min },
                { "max", max },
                { "replacement", replacement },
                { "base", integerBase },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedIntegerMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, IntegerMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for uniform integer sequence methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject IntegerSequenceHelper(int n, int length, int min, int max, bool replacement = true, int integerBase = 10, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "length", length },
                { "min", min },
                { "max", max },
                { "replacement", replacement },
                { "base", integerBase },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedIntegerSequenceMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for (optionally) multiform integer sequence methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject IntegerSequenceHelper(int n, int[] length, int[] min, int[] max, bool[] replacement = null, int[] integerBase = null, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            if (replacement == null)
            {
                replacement = FillArray(new bool[n], DefaultReplacement);
            }

            if (integerBase == null)
            {
                integerBase = FillArray(new int[n], DefaultIntBase);
            }

            JObject request = new JObject
            {
                { "n", n },
                { "length", JArray.FromObject(length) },
                { "min", JArray.FromObject(min) },
                { "max", JArray.FromObject(max) },
                { "replacement", JArray.FromObject(replacement) },
                { "base", JArray.FromObject(integerBase) },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedIntegerSequenceMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, IntegerSequenceMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for decimal fraction methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject DecimalFractionHelper(int n, int decimalPlaces, bool replacement = true, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "decimalPlaces", decimalPlaces },
                { "replacement", replacement },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedDecimalFractionMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, DecimalFractionMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for Gaussian methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject GaussianHelper(int n, double mean, double standardDeviation, int significantDigits, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "mean", mean },
                { "standardDeviation", standardDeviation },
                { "significantDigits", significantDigits },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedGaussianMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, GaussianMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for string methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject StringHelper(int n, int length, string characters, bool replacement = true, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = true)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "length", length },
                { "characters", characters },
                { "replacement", replacement },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedStringMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, StringMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for UUID methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject UUIDHelper(int n, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);

                request = this.GenerateKeyedRequest(request, SignedUUIDMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, UUIDMethod);
            }

            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to generate and send requests for BLOB methods. 
        /// </summary>
        /// <returns>JObject returned from the server</returns>
        private JObject BlobHelper(int n, int size, string format = BlobFormatBase64, JObject pregeneratedRandomization = null, JObject licenseData = null, JObject userData = null, string ticketId = null, bool signed = false)
        {
            JObject request = new JObject
            {
                { "n", n },
                { "size", size },
                { "format", format },
                { "pregeneratedRandomization", pregeneratedRandomization }
            };

            if (signed)
            {
                request.Add("licenseData", licenseData);
                request.Add("userData", userData);
                request.Add("ticketId", ticketId);
                request = this.GenerateKeyedRequest(request, SignedBlobMethod);
            }
            else
            {
                request = this.GenerateKeyedRequest(request, BlobMethod);
            }


            return this.SendRequest(request);
        }

        /// <summary>
        /// Helper function to get the current time (UTC) in milliseconds
        /// </summary>
        /// <returns>Time between Jan. 1, 1970 and now (UTC) </returns>
        private long CurrentTimeMillis()
        {
            DateTime jan1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan javaSpan = DateTime.UtcNow - jan1970;
            return (long)javaSpan.TotalMilliseconds;
        }

        /// <summary>
        /// Helper function to get midnight (UTC) in milliseconds
        /// </summary>
        /// <returns>Time between Jan. 1, 1970 and now (UTC) </returns>
        private long UtcMidnightMillis()
        {
            DateTime jan1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime utcNow = DateTime.UtcNow;
            DateTime utcMidnight = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day + 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan javaSpan = utcMidnight - jan1970;
            return (long)javaSpan.TotalMilliseconds;
        }

        /// <summary>
        /// Helper function for createIntegerSequenceCache with array ([]) parameters
        /// </summary>
        /// <returns>an array which contains the contents of a repeated n times</returns>
        private int[] Adjust(int[] a, int n)
        {
            int[] adjusted = new int[n];
            for (int i = 1, k = 0; i <= n / a.Length; i++)
            {
                for (int j = 0; j < a.Length; j++)
                {
                    adjusted[k++] = a[j];
                }
            }
            return adjusted;
        }

        /// <summary>
        /// Helper function for createIntegerSequenceCache with array ([]) parameters
        /// </summary>
        /// <returns>an array which contains the contents of a repeated n times</returns>
        private bool[] Adjust(bool[] a, int n)
        {
            bool[] adjusted = new bool[n];

            for (int i = 1, k = 0; i <= n / a.Length; i++)
            {
                for (int j = 0; j < a.Length; j++)
                {
                    adjusted[k++] = a[j];
                }
            }
            return adjusted;
        }

        /// <summary>
        /// Helper function to make a string URL-safe (Percent-Encoding as described in RFC 3986 for PHP) and to,
        /// optionally, base64-encode the string.
        /// </summary>
        /// <returns>URL-safe (optionally encoded) version of the initial string s</returns>
        private static string UrlFormatting(string s)
        {
            string pattern = "^([A-Za-z0-9+/]{4})*([A-Za-z0-9+/]{3}=|[A-Za-z0-9+/]{2}==)?$";
            Regex rx = new Regex(pattern);

            if (!rx.IsMatch(s))
            {
                var sBytes = System.Text.Encoding.UTF8.GetBytes(s);
                s = System.Convert.ToBase64String(sBytes);
            }

            s = s.Replace("=", "%3D");
            s = s.Replace("+", "%2B");
            s = s.Replace("/", "%2F");

            return s;
        }

        /// <summary>
        /// Helper function to create HTML code with input tag.
        /// </summary>
        /// <returns>string with input tag and the parameters passed</returns>
        private static string HtmlInput(string type, string name, string value)
        {
            return ("<input type='" + type + "' name='" + name + "' value='" + value + "' />");
        }

        private static int[] FillArray(int[] a, int val)
        {
            for(int i = 0; i < a.Length; i++)
            {
                a[i] = val;
            }

            return a;
        }

        private static bool[] FillArray(bool[] a, bool val)
        {
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = val;
            }

            return a;
        } 
    }
}
