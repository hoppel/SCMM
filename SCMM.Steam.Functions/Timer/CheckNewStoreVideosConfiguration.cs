﻿
namespace SCMM.Steam.Functions.Timer;

public class CheckNewStoreVideosConfiguration
{
    public ChannelExpression[] Channels { get; set; }

    public class ChannelExpression
    {
        public ChannelType Type { get; set; }

        public string ChannelId { get; set; }

        public string Query { get; set; }
    }

    public enum ChannelType
    {
        YouTube = 0,
        Twitch = 1
    }
}
