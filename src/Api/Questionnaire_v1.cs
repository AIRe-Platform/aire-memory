// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Net;
using System.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Platform.Clients;
using Aire.Sdk.Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Helpers;

namespace Aire.Memory.Api;

public class Questionnaire_v1
{
    private readonly BlobContainerClient _questionnaires;
    private readonly ITableStorageService _tables;
    private readonly IJwtTokenService _jwt;
    private readonly IAireClientFactory _clientFactory;
    private readonly ILogger _log;

    public Questionnaire_v1(
        BlobServiceClient blobs,
        ITableStorageService tables,
        IJwtTokenService jwt,
        IAireClientFactory clientFactory,
        ILogger<Questionnaire_v1> log)
    {
        _questionnaires = blobs.GetBlobContainerClient(AireConstants.Blobs.Questionnaires);
        _questionnaires.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _tables = tables;
        _jwt = jwt;
        _clientFactory = clientFactory;
        _log = log;
    }

    [Function("GetQuestionnaires_v1")]
    [OpenApiOperation(
        operationId: "getQuestionnaires",
        tags: ["Questionnaires"],
        Summary = "Get a list of questionnaires")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetQuestionnaires(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaires")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        var all = await _tables.All<QuestionnaireEntity>();
        var models = all.Select(x => x.ToModelAsync(_questionnaires)).ToAsyncEnumerable();
        var list = await models.SelectAwait(async x => await x).ToListAsync();

        return new ObjectResult(list);
    }

    [Function("GetQuestionnairesWithKeyword_v1")]
    [OpenApiOperation(
        operationId: "getQuestionnairesWithKeyword",
        tags: ["Questionnaires"],
        Summary = "Get questionnaires with keyword"
    )]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(
        HttpStatusCode.OK,
        "application/json",
        typeof(List<Questionnaire>),
        Description = "List of questionnaires containing querried keyword")]
    [OpenApiParameter("keyword", Description = "Keyword to query", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetQuestionnairesWithKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaires/{keyword}")] HttpRequest req,
        FunctionContext context,
        string keyword)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth is null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        keyword = KeywordHelper.Sanitize(keyword);
        if (string.IsNullOrWhiteSpace(keyword))
            return new BadRequestResult();

        var questionnaires = new List<Questionnaire>();
        var indexes = await _tables.Partition<KeywordIndexEntity>(
            KeywordIndexEntity.PartitionForResource(ResourceTypes.Questionnaire, keyword)!);
        if (indexes is null || indexes.Count == 0)
            return new ObjectResult(questionnaires);

        foreach (var index in indexes)
        {
            if (index.RowKey is null)
                continue;

            var entity = await _tables.RetrieveAsync<QuestionnaireEntity>(index.RowKey);
            if (entity is null)
                continue;

            questionnaires.Add(await entity.ToModelAsync(_questionnaires));
        }

        return new ObjectResult(questionnaires);
    }

    [Function("GetQuestionnaireWithId_v1")]
    [OpenApiOperation(
        operationId: "getQuestionnaireWithId",
        tags: ["Questionnaires"],
        Summary = "Retrieve a questionnaire")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetQuestionnaireWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var entity = await _tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var model = await entity.ToModelAsync(_questionnaires);
        return new ObjectResult(model);
    }

    [Function("QueryQuestionnaire_v1")]
    [OpenApiOperation(
        operationId: "queryQuestionnaire",
        tags: ["Questionnaires"],
        Summary = "Query questionnaires"
    )]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
    [OpenApiParameter("query",
        CollectionDelimiter = OpenApiParameterCollectionDelimiterType.Comma,
        In = ParameterLocation.Query,
        Required = true,
        Description = "List of keywords separated by commas")]
    [OpenApiParameter("lang",
        In = ParameterLocation.Query,
        Required = false,
        Description = "Set to return questionnaires in a specific language")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Best matching questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> QueryQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string query,
        [FromQuery] string? lang = null)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(query))
            return new BadRequestResult();

        var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
        if (aiService == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var queryWords = query.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var queryResponse = await aiService.QueryQuestionnaires(queryWords);

        if (queryResponse == null || queryResponse.Results == null)
            return new NotFoundResult();

        var questionnaireId = queryResponse.Results
            .Where(x => x.Relevance.HasValue && x.Relevance.Value > 0.7)
            .Where(x => string.IsNullOrEmpty(lang) || lang == x.Language)
            .Select(x => x.Id)
            .FirstOrDefault();

        if (questionnaireId == null)
            return new NotFoundResult();

        var questionnaire = await _tables.RetrieveAsync<QuestionnaireEntity>(questionnaireId);
        if (questionnaire == null)
            return new NotFoundResult();

        var model = await questionnaire.ToModelAsync(_questionnaires);
        return new ObjectResult(model);
    }


    [Function("PostQuestionnaire_v1")]
    [OpenApiOperation(
        operationId: "postQuestionnaire",
        tags: ["Questionnaires"],
        Summary = "Store new questionnaire")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Saved questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/questionnaire")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
            return new ForbiddenResult();

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
        if (aiService == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        questionnaire.Id = Guid.NewGuid();
        var entity = new QuestionnaireEntity(questionnaire);

        {
           var words = await KeywordHelper.UpdateKeywords(
                _tables,
                ResourceTypes.Questionnaire,
                entity.Id(),
                [],
                questionnaire.Keywords ?? []);

            entity.Keywords = string.Join(",", words);
        }

        {
            var embedResult = await aiService.CreateQuestionnaireEmbedding(questionnaire);
            var embedId = embedResult?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings for the questionnaire");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        if (questionnaire.Content == null)
            return new BadRequestResult();

        await entity.SaveToBlob(_questionnaires, questionnaire.Content);

        var add = await _tables.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        return new ObjectResult(questionnaire);
    }


    [Function("PutQuestionnaire_v1")]
    [OpenApiOperation(
            operationId: "putQuestionnaire",
            tags: ["Questionnaires"],
            Summary = "Edit existing questionnaire")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "Questionnaire", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PutQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/questionnaire/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        var aiService = await _clientFactory.CreateAiClient(auth.JwtEncodedToken);
        if (aiService == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var entity = await _tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        // Update entity data

        if (questionnaire.Lang != null)
            entity.Lang = questionnaire.Lang;

        if (questionnaire.Keywords != null)
        {
            var originalKeywords = entity.Keywords?.Split(",");

            var words = await KeywordHelper.UpdateKeywords(
                _tables,
                ResourceTypes.Questionnaire,
                entity.Id(),
                originalKeywords ?? [],
                questionnaire.Keywords);

            entity.Keywords = string.Join(",", words);
        }

        if (questionnaire.Content != null)
            await entity.SaveToBlob(_questionnaires, questionnaire.Content);

        if (questionnaire.Name != null)
            entity.Name = questionnaire.Name;

        // Update embedding

        if (entity.EmbeddingId != null)
        {
            bool result = await aiService.DeleteQuestionnaireEmbedding(entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete questionnaire embeddings");
                return new InternalServerErrorResult();
            }
        }

        var embed = await aiService.CreateQuestionnaireEmbedding(questionnaire);
        var embedId = embed?.Ids?.FirstOrDefault();
        if (embedId == null)
        {
            _log.LogCritical("Failed to create embeddings for the questionnaire");
            return new InternalServerErrorResult();
        }
        entity.EmbeddingId = embedId;

        // Apply edits

        var save = await _tables.UpsertAsync(entity);
        if (!save)
            return new InternalServerErrorResult();

        return new ObjectResult(questionnaire);
    }


    [Function("DeleteQuestionnaire_v1")]
    [OpenApiOperation(
        operationId: "deleteQuestionnaireWithId",
        tags: ["Questionnaires"],
        Summary = "Delete a questionnaire")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/questionnaire/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteQuestionnaire))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var entity = await _tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        {
            await KeywordHelper.UpdateKeywords(
                _tables,
                ResourceTypes.Questionnaire,
                entity.Id(),
                entity.Keywords?.Split(",") ?? [],
                []);
        }

        if (entity.EmbeddingId != null)
        {
            var aiService = await _clientFactory.CreateAiClient(auth.JwtEncodedToken);
            if (aiService == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            bool result = await aiService.DeleteQuestionnaireEmbedding(entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete questionnaire embeddings");
                return new InternalServerErrorResult();
            }
        }

        await _questionnaires.DeleteBlobIfExistsAsync(entity.Id());

        var delete = await _tables.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
