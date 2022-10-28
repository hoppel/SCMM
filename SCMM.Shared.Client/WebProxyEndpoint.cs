﻿namespace SCMM.Shared.Client;

public class WebProxyEndpoint
{
    public string Url { get; set; }

    public string Domain { get; set; }

    public string Username { get; set; }

    public string Password { get; set; }

    public bool IsEnabled { get; set; } = true;
}