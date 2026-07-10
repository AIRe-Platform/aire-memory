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
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Helpers;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Platform;
using Aire.Memory.Services;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class Questionnaire_v1
{
    private readonly BlobContainerClient _questionnaires;
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;
    private readonly IAirePlatformService _platform;
    private readonly IAireClientFactory _clientFactory;
    private readonly IAireModuleSettingsService _moduleConfigService;
    private readonly ILogger _log;

    public Questionnaire_v1(
        BlobServiceClient blobs,
        MemoryStorageService storageService,
        IJwtTokenService jwt,
        IAirePlatformService platformService,
        IAireClientFactory clientFactory,
        IAireModuleSettingsService moduleConfigService,
        ILogger<Questionnaire_v1> log)
    {
        _questionnaires = blobs.GetBlobContainerClient(AireConstants.Blobs.Questionnaires);
        _questionnaires.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _jwt = jwt;
        _platform = platformService;
        _clientFactory = clientFactory;
        _moduleConfigService = moduleConfigService;
        _log = log;
    }

    [Function("GetQuestionnaires_v1")]
    [OpenApiOperation("getQuestionnaires", ["Questionnaires"], Summary = "Get a list of questionnaires")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing platform authentication")]
    public async Task<IActionResult> GetQuestionnaires(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaires")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var all = await tables.All<QuestionnaireEntity>();
        var list = await all
            .ToAsyncEnumerable()
            .Select(async (QuestionnaireEntity x, CancellationToken ct) => await x.ToModelAsync(_questionnaires))
            .ToListAsync();

        return new ObjectResult(list);
    }

    [Function("GetQuestionnairesWithKeyword_v1")]
    [OpenApiOperation("getQuestionnairesWithKeyword", ["Questionnaires"], Summary = "Get questionnaires with keyword")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(
        HttpStatusCode.OK,
        "application/json",
        typeof(List<Questionnaire>),
        Description = "List of questionnaires containing querried keyword")]
    [OpenApiParameter("keyword", Description = "Keyword to query", In = ParameterLocation.Path, Required = true)]
    [OpenApiParameter("lang",
        In = ParameterLocation.Query,
        Required = false,
        Description = "Set to return questionnaires in a specific language")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetQuestionnairesWithKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaires/{keyword}")] HttpRequest req,
        FunctionContext context,
        string keyword,
        [FromQuery] string? lang = null)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth is null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        keyword = KeywordHelper.Sanitize(keyword);
        if (string.IsNullOrWhiteSpace(keyword))
            return new BadRequestResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var questionnaires = new List<Questionnaire>();
        var indexes = await tables.Partition<KeywordIndexEntity>(
            KeywordIndexEntity.PartitionForResource(ResourceTypes.Questionnaire, keyword)!);
        if (indexes is null || indexes.Count == 0)
            return new ObjectResult(questionnaires);

        foreach (var index in indexes)
        {
            if (index.RowKey is null)
                continue;

            var entity = await tables.RetrieveAsync<QuestionnaireEntity>(index.RowKey);
            if (entity is null || (lang != null && entity.Lang != lang) || entity.IsFeedback)
                continue;

            questionnaires.Add(await entity.ToModelAsync(_questionnaires));
        }

        return new ObjectResult(questionnaires);
    }

    [Function("GetQuestionnaireWithId_v1")]
    [OpenApiOperation("getQuestionnaireWithId", ["Questionnaires"], Summary = "Retrieve a questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param or missing platform authentication")]
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

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var model = await entity.ToModelAsync(_questionnaires);
        return new ObjectResult(model);
    }

    [Function("QueryQuestionnaire_v1")]
    [OpenApiOperation("queryQuestionnaire", ["Questionnaires"], Summary = "Query questionnaires")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query or missing platform authentication")]
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

        if (auth.Platform == null)
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(
            auth.Platform, ModuleType.Memory, req.GetTargetService(),
            ModuleSettings.Memory_VectorDbName);

        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            return new InternalServerErrorResult();
        }

        int relevance = await _moduleConfigService.Get<int>(
            auth.Platform, ModuleType.Memory, targetService,
            ModuleSettings.Memory_VectorSearchRelevanceThreshold);

        var queryWords = query.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var queryResponse = await aiService.QueryQuestionnaires(aiDatabase, queryWords, relevance / 100.0f);

        if (queryResponse == null || queryResponse.Results == null)
            return new NotFoundResult();

        var questionnaireId = queryResponse.Results
            .Where(x => string.IsNullOrEmpty(lang) || lang == x.Language)
            .Select(x => x.Id)
            .FirstOrDefault();

        if (questionnaireId == null)
            return new NotFoundResult();

        var questionnaire = await tables.RetrieveAsync<QuestionnaireEntity>(questionnaireId);
        if (questionnaire == null)
            return new NotFoundResult();

        var model = await questionnaire.ToModelAsync(_questionnaires);
        return new ObjectResult(model);
    }

    [Function("QueryFeedbackQuestionnaire_v1")]
    [OpenApiOperation("queryFeedbackQuestionnaire", ["Questionnaires"], Summary = "Query feedback questionnaires")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
    [OpenApiParameter("lang",
        In = ParameterLocation.Query,
        Required = false,
        Description = "Set to return feedback questionnaires in a specific language")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Feedback questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing or invalid parameters, or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> QueryFeedbackQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/feedback-questionnaire")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string? lang = null)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var feedbackQuestionnaires = await tables.QueryAsync<QuestionnaireEntity>(q => q.IsFeedback == true);
        var feedbackQuestionnaire = await feedbackQuestionnaires.Where(q => q.Lang == lang).FirstOrDefaultAsync();

        // Fallback to feedback questionnaires in English
        feedbackQuestionnaire ??= await feedbackQuestionnaires
                .Where(q => q.Lang == "en")
                .FirstOrDefaultAsync();

        if (feedbackQuestionnaire == null)
            return new NotFoundResult();

        // Map the questionnaire entity to the model
        var model = await feedbackQuestionnaire.ToModelAsync(_questionnaires);

        return new ObjectResult(model);
    }


    [Function("PostQuestionnaire_v1")]
    [OpenApiOperation("postQuestionnaire", ["Questionnaires"], Summary = "Store new questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Saved questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or missing platform authentication")]
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

        if (auth.Platform == null)
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        questionnaire.Id = Guid.NewGuid();
        var entity = new QuestionnaireEntity(questionnaire);

        var keywords = KeywordHelper.Sanitize(questionnaire.Keywords ?? []);
        entity.Keywords = string.Join(",", keywords);

        // Add questionnaires to vector index (except for feedback surveys)
        if (!entity.IsFeedback)
        {
            var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
            if (aiModule == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
            var aiDatabase = await _moduleConfigService.Get<string>(
                auth.Platform, ModuleType.Memory, targetService,
                ModuleSettings.Memory_VectorDbName);

            if (aiDatabase == null)
            {
                _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
                return new InternalServerErrorResult();
            }

            var embedResult = await aiService.CreateQuestionnaireEmbedding(aiDatabase, questionnaire);
            var embedId = embedResult?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings for the questionnaire");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        // Must have either an external url or content
        if (string.IsNullOrEmpty(questionnaire.ExternalUrl))
        {
            if (questionnaire.Content == null)
                return new BadRequestResult();

            await entity.SaveToBlob(_questionnaires, questionnaire.Content);
        }
        else
        {
            if (questionnaire.Content != null)
                return new BadRequestResult();
        }

        var add = await tables.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        // Update keyword index
        await KeywordHelper.UpdateKeywords(tables, ResourceTypes.Questionnaire, entity.Id(), [], keywords);

        return new ObjectResult(questionnaire);
    }


    [Function("PutQuestionnaire_v1")]
    [OpenApiOperation("putQuestionnaire", ["Questionnaires"], Summary = "Edit existing questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "Questionnaire", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param, or missing platform authentication")]
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

        if (auth.Platform == null)
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(
            auth.Platform, ModuleType.Memory, targetService,
            ModuleSettings.Memory_VectorDbName);

        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            return new InternalServerErrorResult();
        }

        var entity = await tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        // Update entity data
        if (questionnaire.Lang != null)
            entity.Lang = questionnaire.Lang;

        string[]? keywords = null;
        var originalKeywords = entity.Keywords?.Split(",") ?? [];
        if (questionnaire.Keywords != null)
        {
            keywords = KeywordHelper.Sanitize(questionnaire.Keywords);
            entity.Keywords = string.Join(",", keywords);
        }

        if (questionnaire.Content != null)
        {
            if (!string.IsNullOrEmpty(questionnaire.ExternalUrl))
                return new BadRequestResult();

            // Update content
            await entity.SaveToBlob(_questionnaires, questionnaire.Content);
            entity.ExternalUrl = "";
        }
        else if (!string.IsNullOrEmpty(questionnaire.ExternalUrl))
        {
            // if the questionnaire was changed to an external URL, 
            // make sure to erase old content
            await entity.DeleteBlob(_questionnaires);
            entity.ExternalUrl = questionnaire.ExternalUrl;
        }

        if (questionnaire.Name != null)
            entity.Name = questionnaire.Name;

        // Update embedding
        if (entity.EmbeddingId != null)
        {
            bool result = await aiService.DeleteQuestionnaireEmbedding(aiDatabase, entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete questionnaire embeddings");
                return new InternalServerErrorResult();
            }
        }

        // Exclude feedback questionnaires from the vector index
        if (!entity.IsFeedback)
        {
            var embed = await aiService.CreateQuestionnaireEmbedding(aiDatabase, questionnaire);
            var embedId = embed?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings for the questionnaire");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        // Apply edits
        var save = await tables.UpsertAsync(entity);
        if (!save)
            return new InternalServerErrorResult();

        // Update keywords
        if (keywords != null)
        {
            await KeywordHelper.UpdateKeywords(
                tables, ResourceTypes.Questionnaire, entity.Id(), originalKeywords, keywords);
        }

        return new ObjectResult(questionnaire);
    }


    [Function("DeleteQuestionnaire_v1")]
    [OpenApiOperation("deleteQuestionnaireWithId", ["Questionnaires"], Summary = "Delete a questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param or missing platform authentication")]
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

        if (auth.Platform == null)
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var entity = await tables.RetrieveAsync<QuestionnaireEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        if (entity.EmbeddingId != null)
        {
            var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
            if (aiModule == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
            var aiDatabase = await _moduleConfigService.Get<string>(
                auth.Platform, ModuleType.Memory, targetService,
                ModuleSettings.Memory_VectorDbName);

            if (aiDatabase == null)
            {
                _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
                return new InternalServerErrorResult();
            }

            bool result = await aiService.DeleteQuestionnaireEmbedding(aiDatabase, entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete questionnaire embeddings");
                return new InternalServerErrorResult();
            }
        }

        await entity.DeleteBlob(_questionnaires);

        var delete = await tables.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        // Update keyword index
        {
            var keywords = entity.Keywords?.Split(",") ?? [];
            await KeywordHelper.UpdateKeywords(
                tables, ResourceTypes.Questionnaire, entity.Id(), keywords, []);
        }

        return new NoContentResult();
    }

    [Function("QueryFeedbackLanguages_v1")]
    [OpenApiOperation("queryFeedbackLanguages", ["Questionnaires"], Summary = "Query languages that already have feedback questionnaires")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<string>), Description = "List of languages with feedback questionnaires")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing platform authentication")]
    public async Task<IActionResult> QueryFeedbackLanguages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/feedback-languages")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        // Retrieve all feedback questionnaires
        var feedbackQuestionnaires = await tables.QueryAsync<QuestionnaireEntity>(q => q.IsFeedback == true);

        // Get a list of distinct languages for the feedback questionnaires
        var feedbackLanguages = await feedbackQuestionnaires
            .Select(q => q.Lang)
            .Distinct()
            .ToListAsync();

        return new ObjectResult(feedbackLanguages);
    }
}
