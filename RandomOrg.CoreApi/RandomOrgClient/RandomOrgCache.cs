using System;
using System.Collections.Concurrent;
using System.Threading;

using Newtonsoft.Json.Linq;
using RandomOrg.CoreApi.Errors;

namespace RandomOrg.CoreApi
{
    /// <summary>
    /// Delegate for the request function used in a RandomOrgCache.
    /// </summary>
    /// <param name="request">JSON to send</param>
    /// <returns>JObject response from the server</returns>
    public delegate JObject RequestFunction(JObject request);

    /// <summary>
    /// Delegate for extracting data from the JObjected returned by the server.
    /// </summary>
    /// <param name="response">JObject response</param>
    /// <returns>generally, an array (one- or two-dimensional)</returns>
    public delegate dynamic ProcessFunction(JObject response);

    /// <summary>
    /// Precache class for frequently used requests.
    /// </summary>
    /// <remarks>
    /// *** WARNING **
    /// Instances of this class should only be obtained using a RandomOrgClient's CreateCache() methods. 
    /// This class strives to keep a queue of response results populated for instant access via its public 
    /// <see cref="RandomOrgCache{T}.Get"/> method. Work is done by a background Thread, which issues 
    /// the appropriate request at suitable intervals.
    /// </remarks>
    /// <typeparam name="T">return array type, e.g., int[]</typeparam>
    public class RandomOrgCache<T>
    {
        private readonly RequestFunction requestFunction;
        private readonly ProcessFunction processFunction;

        private readonly JObject request;

        private readonly object threadLock = new object();

        private readonly BlockingCollection<T> queue = new BlockingCollection<T>();
        private readonly int cacheSize;

        private int bulkRequestNumber;
        private readonly int requestNumber, requestSize;

        private bool paused;

        private long usedBits = 0;
        private long usedRequests = 0;

        /// <summary>
        /// Initialize class and start queue population Thread running.
        /// </summary>
        /// <remarks>
        /// ** WARNING** Should only be called by RandomOrgClient's createCache() methods.
        /// </remarks>
        /// <param name="requestFunction">function used to send supplied request to server.</param>
        /// <param name="processFunction">function to process result of requestFunction into expected output.</param>
        /// <param name="request">request to send to server via requestFunction.</param>
        /// <param name="cacheSize">number of request responses to try maintain.</param>
        /// <param name="bulkRequestNumber">if request is set to be issued in bulk, number of result 
        /// sets in a bulk request, else 0.</param>
        /// <param name="requestNumber">if request is set to be issued in bulk, number of results 
        /// in a single request, else 0.</param>
        /// <param name="singleRequestSize">size of a single request in bits for adjusting bulk requests 
        /// if bits are in short supply on the server.</param>
        public RandomOrgCache(RequestFunction requestFunction, ProcessFunction processFunction,
                             JObject request, int cacheSize, int bulkRequestNumber, int requestNumber, int singleRequestSize)
        {

            this.requestFunction = requestFunction;
            this.processFunction = processFunction;

            this.request = request;

            this.cacheSize = cacheSize;

            this.bulkRequestNumber = bulkRequestNumber;
            this.requestNumber = requestNumber;
            this.requestSize = singleRequestSize;

            // Thread to keep RandomOrgCache populated.
            Thread t = new Thread(new ThreadStart(this.PopulateQueue));
            t.Start();
        }

        /// <summary>
        /// Function to continue issuing requests until the queue is full.
        /// </summary>
        /// <remarks>
        /// Keep issuing requests to server until queue is full. When queue is full if requests are being issued 
        /// in bulk, wait until queue has enough space to accommodate all of a bulk request before issuing a new 
        /// request, otherwise issue a new request every time an item in the queue has been consumed.Note that 
        /// requests to the server are blocking, i.e., only one request will be issued by the cache at any given time.
        /// </remarks>
        protected void PopulateQueue()
        {
            while (true)
            {
                lock (this.threadLock)
                {
                    if (this.paused)
                    {
                        try
                        {
                            Monitor.Wait(this.threadLock);
                        }
                        catch (ThreadInterruptedException)
                        {
                            System.Diagnostics.Debug.WriteLine("Cache interrupted while waiting for notify()");
                        }
                    }
                }

                // If we're issuing bulk requests...
                if (this.bulkRequestNumber > 0)
                {
                    // Is there space for a bulk response in the queue?
                    if (this.queue.Count < (this.cacheSize - this.bulkRequestNumber))
                    {

                        // Issue and process request and response.
                        try
                        {
                            JObject response = this.requestFunction(request);

                            dynamic result = this.processFunction(response);

                            // Split bulk response into result sets.
                            int length = result.Length;

                            for (int i = 0; i < length; i += this.requestNumber)
                            {

                                dynamic entry = Array.CreateInstance(result[0].GetType(), this.requestNumber);
                                for (int j = 0; j < this.requestNumber; j++)
                                {
                                    entry[j] = result[i + j];
                                }
                                this.queue.Add(entry);
                            }

                            // update usage counters
                            this.usedBits += (int)((JObject)response["result"])["bitsUsed"];
                            this.usedRequests++;

                        }
                        catch (RandomOrgInsufficientBitsException e)
                        {

                            // get bits left
                            int bits = e.bits;

                            // can we adapt bulk request size?
                            if (bits != -1 && this.requestSize < bits)
                            {

                                this.bulkRequestNumber = bits / this.requestSize;

                                // update bulk request size
                                this.request.Remove("n");
                                this.request.Add("n", this.bulkRequestNumber * this.requestNumber);

                                // no - so error
                            }
                            else
                            {
                                throw e;
                            }

                        }
                        catch (Exception e)
                        {
                            System.Diagnostics.Debug.WriteLine("RandomOrgCache populate Exception: " + e.GetType().ToString() + ": " + e.Message);
                        }
                    }
                    else
                    {
                        // No space, sleep and wait for consumed notification.
                        lock (this.threadLock)
                        {
                            try
                            {
                                Monitor.Wait(this.threadLock);
                            }
                            catch (ThreadInterruptedException)
                            {
                                System.Diagnostics.Debug.WriteLine("Cache interrupted while waiting for notify()");
                            }
                        }
                    }

                    // Not in bulk mode, repopulate queue as it empties.
                }
                else if (this.queue.Count < this.cacheSize)
                {
                    try
                    {
                        JObject response = this.requestFunction(request);

                        this.queue.Add(this.processFunction(response));

                        // update usage counters
                        this.usedBits += (int)((JObject)response["result"])["bitsUsed"];
                        this.usedRequests++;

                    }
                    catch (Exception e)
                    {
                        // Don't handle failures from requestFunction(), Just try again later.
                        System.Diagnostics.Debug.WriteLine("RandomOrgCache populate Exception: " + e.GetType().ToString() + ": " + e.Message);
                    }
                }
                else
                {
                    // No space, sleep and wait for consumed notification.
                    lock (this.threadLock)
                    {
                        try
                        {
                            Monitor.Wait(this.threadLock);
                        }
                        catch (ThreadInterruptedException)
                        {
                            System.Diagnostics.Debug.WriteLine("Cache interrupted while waiting for notify()");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cache will no longer continue to populate itself.
        /// </summary>
        public void Stop()
        {
            lock (this.threadLock)
            {
                this.paused = true;
                Monitor.Pulse(this.threadLock);
            }
        }

        /// <summary>
        /// Cache will resume populating itself if stopped.
        /// </summary>
        public void Resume()
        {
            lock (this.threadLock)
            {
                this.paused = false;
                Monitor.Pulse(this.threadLock);
            }
        }

        /// <summary>
        /// Check if the cache is currently not re-populating itself.
        /// </summary>
        /// <remarks>
        /// Values currently cached may still be retrieved with <see cref="RandomOrgCache{T}.Get"/> but 
        /// no new values are being fetched from the server.
        /// <para/>This state can be changed with <see cref="RandomOrgCache{T}.Stop"/> and 
        /// <see cref="RandomOrgCache{T}.Resume"/>
        /// </remarks>
        /// <returns>true if cache is currently not re-populating itself, false otherwise</returns>
        public bool IsPaused()
        {
            return this.paused;
        }

        /// <summary>
        /// Get next response.
        /// </summary>
        /// <returns>next appropriate response for the request this RandomOrgCache represents or, if queue is 
        /// empty throws a <see cref="RandomOrgCacheEmptyException"/></returns>
        /// <exception cref="RandomOrgCacheEmptyException">Thrown when the queue is empty.</exception>
        public T Get()
        {
            lock (this.threadLock)
            {
                if (this.queue.Count == 0)
                {
                    throw new RandomOrgCacheEmptyException("The RandomOrgCache queue is empty - " +
                        "wait for it to repopulate itself.");
                }
                T result = this.queue.Take();
                Monitor.Pulse(this.threadLock);
                return result;
            }
        }

        /// <summary>
        /// Get next response or wait until the next value is available.
        /// </summary>
        /// <remarks>
        /// This method will block until a value is available. 
        /// <para/>Note: if the cache is paused or no more randomness is available from the server this call can 
        /// result in a dead lock. See <see cref="RandomOrgCache{T}.IsPaused"/>.
        /// </remarks>
        /// <returns>next appropriate response for the request this RandomOrgCache represents</returns>
        /// <exception cref="ThreadInterruptedException">Thrown if any thread interrupted the current thread before 
        /// or while the current thread was waiting for a notification. The interrupted status of the current 
        /// thread is cleared when this exception is thrown.</exception>
        public T GetOrWait()
        {
            // get result or wait
            T result = this.queue.Take();

            // notify cache - check if refill is needed
            lock (this.threadLock)
            {
                Monitor.Pulse(this.threadLock);
            }

            return result;
        }

        /// <summary>
        /// Get number of results of type T remaining in the cache.
        /// </summary>
        /// <remarks>
        /// This essentially returns how often <see cref="RandomOrgCache{T}.Get"/> may be called without 
        /// a cache refill, or <see cref="RandomOrgCache{T}.GetOrWait"/> may be called without blocking.
        /// </remarks>
        /// <returns>current number of cached results</returns>
        public int GetCachedValues()
        {
            return this.queue.Count;
        }

        /// <summary>
        /// Get number of bits used by this cache.
        /// </summary>
        /// <returns>number of used bits</returns>
        public long GetUsedBits()
        {
            return this.usedBits;
        }

        /// <summary>
        /// Get number of requests used by this cache.
        /// </summary>
        /// <returns>number of used requests</returns>
        public long GetUsedRequests()
        {
            return this.usedRequests;
        }
    }
}
