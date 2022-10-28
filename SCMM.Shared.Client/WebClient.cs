﻿using System.Net;
using System.Net.Http.Headers;

namespace SCMM.Shared.Client;

public class WebClient : IDisposable
{
    private readonly IDictionary<string, string> _defaultHeaders;
    private readonly CookieContainer _cookieContainer;
    private readonly IWebProxy _webProxy;
    private readonly HttpMessageHandler _httpHandler;
    private bool _disposedValue;

    public WebClient(CookieContainer cookieContainer = null, IWebProxy webProxy = null)
    {
        _defaultHeaders = new Dictionary<string, string>();
        _cookieContainer = cookieContainer;
        _webProxy = webProxy;
        _httpHandler = new HttpClientHandler()
        {
            UseCookies = cookieContainer != null,
            CookieContainer = cookieContainer ?? new CookieContainer(),
            Proxy = webProxy,
            PreAuthenticate = webProxy != null
        };
    }

    protected HttpClient BuildWebBrowserHttpClient(Uri referer = null)
    {
        var httpClient = BuildHttpClient();

        // We are a normal looking web browser, honest (helps with WAF rules that block bots)
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(Windows NT 10.0; Win64; x64)"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleWebKit", "537.36"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, like Gecko)"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Chrome", "96.0.4664.110"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Safari", "537.36"));

        // NOTE: Don't give them an easy way to identify us
        //DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SCMM", "1.0"));

        // We made this request from a web browser, honest (helps with WAF rules that enforce OWASP)
        httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        if (referer != null)
        {
            // We made this request from your website, honest...
            httpClient.DefaultRequestHeaders.Referrer = referer;
        }

        return httpClient;
    }

    protected HttpClient BuildWebApiHttpClient(string apiKey = null)
    {
        var httpClient = BuildHttpClient();
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        return httpClient;
    }

    protected HttpClient BuildHttpClient()
    {
        var httpClient = new HttpClient(_httpHandler, false);
        if (_defaultHeaders != null)
        {
            foreach (var header in _defaultHeaders)
            {
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        return httpClient;
    }

    public void RotateWebProxy(Uri address, TimeSpan cooldown)
    {
        (_webProxy as IRotatingWebProxy)?.RotateProxy(address, cooldown);
    }

    public void DisableWebProxy(Uri address)
    {
        (_webProxy as IRotatingWebProxy)?.DisableProxy(address);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _httpHandler?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected IDictionary<string, string> DefaultHeaders => _defaultHeaders;

    protected CookieContainer Cookies => _cookieContainer;
}