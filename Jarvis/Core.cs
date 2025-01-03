using NAudio.Wave;
using System.Text;
using Microsoft.SemanticKernel;
using OpenAI.RealtimeConversation;




namespace Jarvis;

public class Core
{
    #pragma warning disable SKEXP0070
    #pragma warning disable OPENAI002

    public async Task GetResponse(Kernel kernel, RealtimeConversationSession session)
    {
        try
        {
            // Initialize dictionaries to store streamed audio responses.
            Dictionary<string, MemoryStream> outputAudioStreamsById = [];

            // Define a loop to receive conversation updates in the session.
            await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync())
            {
                // Notification indicating the start of the conversation session.
                if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                {
                    Console.WriteLine($">>> Session started. ID: {sessionStartedUpdate.SessionId}");
                }

                // Notification indicating the start of detected voice activity.
                if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                {
                    Console.WriteLine($"  -- Voice activity detection started at {speechStartedUpdate.AudioStartTime}");
                }

                // Notification indicating the end of detected voice activity.
                if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                {
                    Console.WriteLine($"  -- Voice activity detection ended at {speechFinishedUpdate.AudioEndTime}");
                }

                // Notification indicating the start of item streaming, such as a function call or response message.
                if (update is ConversationItemStreamingStartedUpdate itemStreamingStartedUpdate)
                {
                    Console.WriteLine("  -- Begin streaming of new item");
                    if (!string.IsNullOrEmpty(itemStreamingStartedUpdate.FunctionName))
                    {
                        Console.Write($"    {itemStreamingStartedUpdate.FunctionName}: ");
                    }
                }

                // Notification about item streaming delta, which may include audio transcript, audio bytes, or function arguments.
                if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate)
                {
                    Console.Write(deltaUpdate.AudioTranscript);
                    Console.Write(deltaUpdate.Text);
                    Console.Write(deltaUpdate.FunctionArguments);

                    // Handle audio bytes.
                    if (deltaUpdate.AudioBytes is not null)
                    {
                        if (!outputAudioStreamsById.TryGetValue(deltaUpdate.ItemId, out MemoryStream value))
                        {
                            value = new MemoryStream();
                            outputAudioStreamsById[deltaUpdate.ItemId] = value;
                        }

                        value.Write(deltaUpdate.AudioBytes);
                    }
                }

                // Notification indicating the end of item streaming, such as a function call or response message.
                // At this point, audio transcript can be displayed on console, or a function can be called with aggregated arguments.
                if (update is ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  -- Item streaming finished, item_id={itemStreamingFinishedUpdate.ItemId}");
                    Console.Write($"    + [{itemStreamingFinishedUpdate.MessageRole}]: ");

                    foreach (ConversationContentPart contentPart in itemStreamingFinishedUpdate.MessageContentParts)
                    {
                        Console.Write(contentPart.AudioTranscript);
                    }

                    Console.WriteLine();
                }

                // Notification indicating the completion of transcription from input audio.
                if (update is ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  -- User audio transcript: {transcriptionCompletedUpdate.Transcript}");
                    Console.WriteLine();
                }

                // Notification about completed model response turn.
                if (update is ConversationResponseFinishedUpdate turnFinishedUpdate)
                {
                    Console.WriteLine($"  -- Model turn generation finished. Status: {turnFinishedUpdate.Status}");

                    // Output the size of received audio data and dispose streams.
                    foreach ((string itemId, Stream outputAudioStream) in outputAudioStreamsById)
                    {
                        Console.WriteLine($"Raw audio output for {itemId}: {outputAudioStream.Length} bytes");

                        // Convert raw PCM data to WAV format
                        var wavHeader = new byte[44];
                        int sampleRate = 22050;
                        short bitsPerSample = 16;
                        short channels = 1;
                        int byteRate = sampleRate * channels * (bitsPerSample / 8);
                        int blockAlign = channels * (bitsPerSample / 8);
                        int subChunk2Size = (int)outputAudioStream.Length;
                        int chunkSize = 36 + subChunk2Size;

                        // RIFF header
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("RIFF"), 0, wavHeader, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(chunkSize), 0, wavHeader, 4, 4);
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("WAVE"), 0, wavHeader, 8, 4);

                        // fmt subchunk
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("fmt "), 0, wavHeader, 12, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(16), 0, wavHeader, 16, 4); // Subchunk1Size (16 for PCM)
                        Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, wavHeader, 20, 2); // AudioFormat (1 for PCM)
                        Buffer.BlockCopy(BitConverter.GetBytes(channels), 0, wavHeader, 22, 2
                        ); // NumChannels
                        Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, wavHeader, 24, 4); // SampleRate
                        Buffer.BlockCopy(BitConverter.GetBytes(byteRate), 0, wavHeader, 28, 4); // ByteRate
                        Buffer.BlockCopy(BitConverter.GetBytes(blockAlign), 0, wavHeader, 32, 2); // BlockAlign
                        Buffer.BlockCopy(BitConverter.GetBytes(bitsPerSample), 0, wavHeader, 34, 2); // BitsPerSample

                        // data subchunk
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("data"), 0, wavHeader, 36, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(subChunk2Size), 0, wavHeader, 40, 4);

                        // Write WAV header to output stream
                        outputAudioStream.Seek(0, SeekOrigin.Begin);
                        outputAudioStream.Write(wavHeader, 0, wavHeader.Length);

                        var outputAudioPath = $"outputaudio_{DateTime.Now:yyyyMMddHHmmss}.wav";
                        using (var fileStream = new FileStream(outputAudioPath, FileMode.Create, FileAccess.Write))
                        {
                            outputAudioStream.Seek(0, SeekOrigin.Begin);
                            await outputAudioStream.CopyToAsync(fileStream);
                            await fileStream.FlushAsync();
                            outputAudioStream.Dispose();
                            outputAudioStreamsById.Remove(itemId);
                        }

                        Console.WriteLine($"Output audio saved to {outputAudioPath}");

                        using (var audioFile = new AudioFileReader(outputAudioPath))
                        using (var outputDevice = new WaveOutEvent())
                        {
                            outputDevice.Init(audioFile);
                            outputDevice.Play();

                            Console.WriteLine("Riproduzione in corso... Premi Enter per terminare.");
                            Console.ReadLine();

                            outputDevice.Stop();
                        }
                    }
                }

                // Notification about error in conversation session.
                if (update is ConversationErrorUpdate errorUpdate)
                {
                    Console.WriteLine();
                    Console.WriteLine($"ERROR: {errorUpdate.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}