using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

public class Program
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private YoutubeClient _youtubeClient;
    private Process _ffmpegProcess;
    private bool _isPlaying;



    public static void Main(string[] args)
    {
        new Program().RunBotAsync().GetAwaiter().GetResult();
    }

    public async Task RunBotAsync()
    {
        _client = new DiscordSocketClient();
        _commands = new CommandService();
        _youtubeClient = new YoutubeClient();
        _isPlaying = false;

        await _client.LoginAsync(TokenType.Bot, "YOUR BOT TOKEN HERE");
        await _client.StartAsync();

        await RegisterCommandsAsync();

        _client.Log += Log;

        await Task.Delay(-1);
    }

    private Task Log(LogMessage arg)
    {
        Console.WriteLine(arg);
        return Task.CompletedTask;
    }

    public async Task RegisterCommandsAsync()
    {
        _client.MessageReceived += HandleCommandAsync;

        await _commands.AddModuleAsync<MusicCommands>(_services);
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        var message = arg as SocketUserMessage;
        var context = new SocketCommandContext(_client, message);

        if (message.Author.IsBot) return;

        int argPos = 0;
        if (message.HasStringPrefix("!", ref argPos))
        {
            var result = await _commands.ExecuteAsync(context, argPos, _services);
            if (!result.IsSuccess)
                Console.WriteLine(result.ErrorReason);
        }
    }
}

public class MusicCommands : ModuleBase<SocketCommandContext>
{
    private readonly YoutubeClient _youtubeClient;
    private readonly Process _ffmpegProcess;
    private bool _isPlaying;

    public MusicCommands(YoutubeClient youtubeClient, Process ffmpegProcess)
    {
        _youtubeClient = youtubeClient;
        _ffmpegProcess = ffmpegProcess;
        _isPlaying = false;
    }

    [Command("play")]
    public async Task PlayAsync([Remainder] string query)
    {
        if (_isPlaying)
        {
            await ReplyAsync("Already playing a song.");
            return;
        }

        var searchResult = await _youtubeClient.Search.GetVideosAsync(query);

        var video = searchResult.FirstOrDefault();
        if (video == null)
        {
            await ReplyAsync("No video found with the given query.");
            return;
        }

        var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
        if (streamInfo == null)
        {
            await ReplyAsync("Could not retrieve a valid stream for the video.");
            return;
        }

        var ffmpegStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{streamInfo.Url}\" -ac 2 -f s16le -ar 48000 pipe:1 -loglevel quiet",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        _ffmpegProcess.StartInfo = ffmpegStartInfo;
        _ffmpegProcess.Start();

        var discordClient = await Context.Guild.GetVoiceChannel(1234567890).ConnectAsync();

        var output = _ffmpegProcess.StandardOutput.BaseStream;
        var stream = discordClient.CreatePCMStream(AudioApplication.Music);
        await output.CopyToAsync(stream);

        await stream.FlushAsync();
        await discordClient.StopAsync();

        _isPlaying = false;
    }

    [Command("stop")]
    public async Task StopAsync()
    {
        if (!_isPlaying)
        {
            await ReplyAsync("Not currently playing a song.");
            return;
        }

        _ffmpegProcess?.Kill();
        _isPlaying = false;

        await ReplyAsync("Stopped playing the song.");
    }
}
