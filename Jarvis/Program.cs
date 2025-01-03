using Jarvis;
using NAudio.Wave;
using System.ClientModel;
using Microsoft.SemanticKernel;
using OpenAI.RealtimeConversation;
using Microsoft.SemanticKernel.ChatCompletion;




#pragma warning disable SKEXP0070
#pragma warning disable OPENAI002

Console.WriteLine("Jarvis, Inizializzazione...");



Core core = new();
string modelId = "gpt-4";
string apiKey = "sk-proj-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"; // Put your OpenAI API key here

var builder = Kernel.CreateBuilder().AddOpenAIChatCompletion(modelId, apiKey);
RealtimeConversationClient realtimeConversationClient = new(model: "gpt-4o-realtime-preview", credential: new ApiKeyCredential(apiKey));

// Build the kernel
Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();


// Start a new conversation session.
RealtimeConversationSession session = await realtimeConversationClient.StartConversationSessionAsync();

// Configure session with defined options.
await session.ConfigureSessionAsync(new()
{
    Voice = ConversationVoice.Echo,
    InputAudioFormat = ConversationAudioFormat.Pcm16,
    OutputAudioFormat = ConversationAudioFormat.Pcm16,

    InputTranscriptionOptions = new()
    {
        Model = "whisper-1",
    },
});
await session.AddItemAsync(ConversationItem.CreateSystemMessage(["Sei un utile assistente digitale chiamato Jarvis. La tua persona è quella di un uomo con uno spiccato senso del umor che risponde con tono ironico alle domende, tipo come Jarvis la IA di Ironman. Puoi usare i plugin, le funzioni e gli strumenti forniti. Rispondi in modo appropriato e non dimenticare di essere ironico."]));


_ = core.GetResponse(kernel, session);

Console.WriteLine("Jarvis, Inizializzazione completata.");
Console.WriteLine();


while (true)
{
    Console.WriteLine("Premi Enter per iniziare a registrare...");
    Console.ReadLine();

    WaveInEvent waveIn = new()
    {
        WaveFormat = new WaveFormat(24000, 16, 1)
    };

    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
    string inputAudioPath = $"input_{timestamp}.wav";

    var writer = new WaveFileWriter(inputAudioPath, waveIn.WaveFormat);


    waveIn.DataAvailable += (s, a) =>
    {
        writer.Write(a.Buffer, 0, a.BytesRecorded);
    };


    waveIn.RecordingStopped += (s, a) =>
    {
        writer.Flush();
        writer.Close();
        writer.Dispose();
        Console.WriteLine("Registrazione completata e salvata in " + inputAudioPath);

        Stream inputAudioStream = File.OpenRead(inputAudioPath);

        _ = session.SendInputAudioAsync(inputAudioStream);

        //File.Delete(inputAudioPath);

        waveIn.Dispose();
    };


    // Avvia la registrazione
    waveIn.StartRecording();

    Console.WriteLine("Recording... Premi Enter per fermare.");
    Console.ReadLine();

    // Termina la registrazione
    waveIn.StopRecording();

}