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
using Newtonsoft.Json;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Platform;

namespace Aire.Memory.Api;

public class Questionnaire_v1
{
    private readonly BlobContainerClient _questionnaires;
    private readonly ITableStorageService _tables;
    private readonly IJwtTokenService _jwt;
    private readonly IAirePlatformService _platform;
    private readonly IAireClientFactory _clientFactory;
    private readonly IAireModuleSettingsService _moduleConfigService;
    private readonly ILogger _log;

    public Questionnaire_v1(
        BlobServiceClient blobs,
        ITableStorageService tables,
        IJwtTokenService jwt,
        IAirePlatformService platformService,
        IAireClientFactory clientFactory,
        IAireModuleSettingsService moduleConfigService,
        ILogger<Questionnaire_v1> log)
    {
        _questionnaires = blobs.GetBlobContainerClient(AireConstants.Blobs.Questionnaires);
        _questionnaires.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _tables = tables;
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
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
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
            if (entity is null || (lang != null && entity.Lang != lang))
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

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire) || auth.Platform == null)
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(query))
            return new BadRequestResult();

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(auth.Platform, ModuleSettings.Memory_VectorDbName);
        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            return new InternalServerErrorResult();
        }

        int relevance = await _moduleConfigService.Get<int>(auth.Platform, ModuleSettings.Memory_VectorSearchRelevanceThreshold);
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

        var questionnaire = await _tables.RetrieveAsync<QuestionnaireEntity>(questionnaireId);
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
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing or invalid parameters")]
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

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire) || auth.Platform == null)
            return new ForbiddenResult();

        // Retrieve all feedback questionnaires matching the IsFeedback = true condition
        var feedbackQuestionnaires = await _tables.QueryAsync<QuestionnaireEntity>(q => q.IsFeedback == true);

        //filter by language
        var feedbackQuestionnaire = await feedbackQuestionnaires.Where(q => q.Lang == lang).FirstOrDefaultAsync();

        //if not in seleected language, then English
        if (feedbackQuestionnaire == null)
        {
            feedbackQuestionnaire = await feedbackQuestionnaires
                .Where(q => q.Lang == "en")
                .FirstOrDefaultAsync();
        }

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

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire) || auth.Platform == null)
            return new ForbiddenResult();

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        Console.WriteLine("Received Questionnaire:");
        Console.WriteLine(JsonConvert.SerializeObject(questionnaire, Formatting.Indented));

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(auth.Platform, ModuleSettings.Memory_VectorDbName);
        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
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
            var embedResult = await aiService.CreateQuestionnaireEmbedding(aiDatabase, questionnaire);
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
    [OpenApiOperation("putQuestionnaire", ["Questionnaires"], Summary = "Edit existing questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire) || auth.Platform == null)
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(auth.Platform, ModuleSettings.Memory_VectorDbName);
        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
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

        entity.IsFeedback = questionnaire.IsFeedback;

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

        var embed = await aiService.CreateQuestionnaireEmbedding(aiDatabase, questionnaire);
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
    [OpenApiOperation("deleteQuestionnaireWithId", ["Questionnaires"], Summary = "Delete a questionnaire")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteQuestionnaire) || auth.Platform == null)
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
            var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
            if (aiModule == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
            var aiDatabase = await _moduleConfigService.Get<string>(auth.Platform, ModuleSettings.Memory_VectorDbName);
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

        await _questionnaires.DeleteBlobIfExistsAsync(entity.Id());

        var delete = await _tables.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

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
    public async Task<IActionResult> QueryFeedbackLanguages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/feedback-languages")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
            return new ForbiddenResult();

        // Retrieve all feedback questionnaires
        var feedbackQuestionnaires = await _tables.QueryAsync<QuestionnaireEntity>(q => q.IsFeedback == true);

        // Get a list of distinct languages for the feedback questionnaires
        var feedbackLanguages = await feedbackQuestionnaires
            .Select(q => q.Lang)
            .Distinct()
            .ToListAsync();

        return new ObjectResult(feedbackLanguages);
    }
}
