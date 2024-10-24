using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using ConsoleAppFramework;

static class Constants {
    public const char terminationCharacter = '\n';
    public const int numberOfSpammers = 10;
    public const int readBufferSize = 1_024;
    public const int numberOfSpamMessages = 10;
    public const int msDelaySpamming = 200;
    public const int networkStreamReadTimeout = 200;
    public const int networkStreamWriteTimeout = 200;
    public const int sReadLoopTimeLimit = 1;
}

static class Program {

    static async Task SpammerAsync(NetworkStream networkStream,
                                   SemaphoreSlim semaphore,
                                   int spammerIndex,
                                   CancellationToken cancellationToken,
                                   int numberOfSpamMessages = Constants.numberOfSpamMessages,
                                   int msDelaySpamming = Constants.msDelaySpamming) {
        Console.WriteLine($"Spammer{spammerIndex} start");

        byte[] readBuffer = new byte[Constants.readBufferSize];
        TimeSpan readLoopTimeLimit = TimeSpan.FromSeconds(Constants.sReadLoopTimeLimit);
        Stopwatch stopwatch = new();

        while (!cancellationToken.IsCancellationRequested) {
            for (int i = 0; i < numberOfSpamMessages; i++) {
                string writeData = $"Spammer{spammerIndex}[{i}] spam";
                var writeBuffer = Encoding.UTF8.GetBytes($"{writeData}\n");

                await semaphore.WaitAsync(cancellationToken: cancellationToken);

                try {
                    Console.WriteLine($"Spammer{spammerIndex}[{i}] write: '{writeData}'");
                    networkStream.Write(writeBuffer, 0, writeBuffer.Length);
                } catch (IOException) {
                    semaphore.Release();
                    throw;
                }

                string readData = "";
                stopwatch.Restart();
                while (true) {
                    try {
                        int readLength = networkStream.Read(readBuffer, 0, readBuffer.Length);
                        if (readLength > 0) {
                            readData += Encoding.UTF8.GetString(readBuffer, 0, readLength);
                        };
                        if (readData.Contains(Constants.terminationCharacter)) {
                            string readLine = readData.Split(Constants.terminationCharacter)[0];
                            Console.WriteLine($"Spammer{spammerIndex}[{i}] read: '{readLine}'");
                            break;
                        };
                        if (stopwatch.Elapsed >= readLoopTimeLimit) {
                            Console.WriteLine($"Spammer{spammerIndex}[{i}] read timeout");
                            break;
                        }
                    } catch (IOException) {
                        stopwatch.Reset();
                        semaphore.Release();
                        throw;
                    }
                }
                stopwatch.Reset();

                semaphore.Release();

                Console.WriteLine($"Spammer{spammerIndex}[{i}] delay for {msDelaySpamming}ms");
                await Task.Delay(millisecondsDelay: msDelaySpamming, cancellationToken: cancellationToken);
            }
        }
        Console.WriteLine($"Spammer{spammerIndex} stop");
    }

    static async Task StartSpammingAsync(NetworkStream networkStream,
                                         CancellationToken cancellationToken,
                                         int numberOfSpammers = Constants.numberOfSpammers,
                                         int msDelaySpamming = Constants.msDelaySpamming) {
        SemaphoreSlim semaphore = new(1);

        List<Task> spammersPool = [];
        for(int i = 0; i < numberOfSpammers; i++) {
            Console.WriteLine($"create Spammer{i} task and add it to the spammers pool");
            spammersPool.Add(SpammerAsync(networkStream: networkStream,
                                          semaphore: semaphore,
                                          spammerIndex: i,
                                          cancellationToken: cancellationToken,
                                          msDelaySpamming: msDelaySpamming + i*10));
        }
        await Task.WhenAll(spammersPool);

        cancellationToken.ThrowIfCancellationRequested();
    }

    static async Task Main(string[] args) {
        await ConsoleApp.RunAsync(args, async (string ipAddress = "127.0.0.1", int port = 31415) => {
            using CancellationTokenSource cts = new();
            Console.CancelKeyPress += (sender, e) => {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nctrl+c pressed, stopping application");
            };
            Console.WriteLine("press ctrl+c to stop the application");

            Console.WriteLine($"ip address: '{ipAddress}'");
            Console.WriteLine($"port: '{port}'");

            IPEndPoint ipEndPoint = new(IPAddress.Parse(ipAddress), port);
            using TcpClient tcpClient = new();
            await tcpClient.ConnectAsync(ipEndPoint);
            await using NetworkStream networkStream = tcpClient.GetStream();
            networkStream.ReadTimeout = Constants.networkStreamReadTimeout;
            networkStream.WriteTimeout = Constants.networkStreamWriteTimeout;

            try {
                await StartSpammingAsync(networkStream: networkStream, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException) {
                Console.WriteLine("spamming operation cancelled");
            }
            finally {
                Console.WriteLine("application stopped");
            }
        });
    }
}
