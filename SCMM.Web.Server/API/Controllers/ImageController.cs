﻿using AutoMapper;
using CommandQuery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using SCMM.Steam.Data.Store;

namespace SCMM.Web.Server.API.Controllers
{
    // TODO: Delete this...
    [ApiController]
    [Route("api/image")]
    [Obsolete("Images are now served from https://data.scmm.app, this API will be removed soon")]
    public class ImageController : ControllerBase
    {
        private readonly ILogger<ImageController> _logger;
        private readonly SteamDbContext _db;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IQueryProcessor _queryProcessor;
        private readonly IMapper _mapper;

        public ImageController(ILogger<ImageController> logger, SteamDbContext db, ICommandProcessor commandProcessor, IQueryProcessor queryProcessor, IMapper mapper)
        {
            _logger = logger;
            _db = db;
            _commandProcessor = commandProcessor;
            _queryProcessor = queryProcessor;
            _mapper = mapper;
        }

        /// <summary>
        /// Get a cached image (DEPRECATED)
        /// </summary>
        /// <param name="id">Image GUID</param>
        /// <remarks>Range requests are supported.</remarks>
        /// <returns>Image data</returns>
        /// <response code="200">If the image is valid, the response body will contain the image data. The <code>Content-Type</code> header will contain the image mime-type. The <code>Expires</code> header will contain the image UTC expiry date (if any).</response>
        /// <response code="404">If the image cannot be found or has expired.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [Obsolete("Images are now served from https://data.scmm.app, this API will be removed soon")]
        [AllowAnonymous]
        [HttpGet("{id}")]
        [HttpGet("{id}.{ext}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetImage(Guid id)
        {
            var image = await _db.FileData.FindAsync(id);
            if (image != null && image.Data?.Length > 0)
            {
                if (image.ExpiresOn != null)
                {
                    Response.Headers.Add(HeaderNames.Expires, new StringValues(image.ExpiresOn.Value.UtcDateTime.Ticks.ToString()));
                }
                return File(image.Data, image.MimeType, image.Name, true);
            }
            else
            {
                return NotFound();
            }
        }
    }
}
