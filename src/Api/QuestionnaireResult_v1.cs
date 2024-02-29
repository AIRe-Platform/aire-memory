using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth.Models;
using Aire.Sdk.Auth.Scopes;
using Aire.Sdk.Auth.Services;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Api
{
    public class QuestionnaireResults_v1
    {
        private readonly DatabaseContext _db;
        private readonly IJwtTokenService _jwt;
        private readonly ILogger _log;

        public QuestionnaireResults_v1(DatabaseContext db, IJwtTokenService jwt, ILoggerFactory loggerFactory)
        {
            _db = db;
            _jwt = jwt;
            _log = loggerFactory.CreateLogger<QuestionnaireResults_v1>();
        }

        [Function("GetQuestionnaireResults_v1")]
        [OpenApiOperation(
                    operationId: "getQuestionnaireResults",
                    tags: ["questionnaire", "results"],
                    Summary = "Retrieve a questionnaire results")]
        [OpenApiSecurity(
                    schemeName: "bearer_auth",
                    schemeType: SecuritySchemeType.Http,
                    Scheme = OpenApiSecuritySchemeType.Bearer,
                    BearerFormat = "JWT",
                    Description = "User token")]
        [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(QuestionnaireResults), Description = "Questionnaire results")]
        [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire or results was not found.")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> GetQuestionnaireResults(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire-results/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadChatHistory))
                return new UnauthorizedResult();

            if (!Guid.TryParse(id, out Guid questionnaireId))
                return new BadRequestResult();

            var ent = await _db.QuestionnaireResults
                .Where(x => x.UserId == auth!.User)
                .Where(x => x.QuestionnaireId == questionnaireId)
                .FirstOrDefaultAsync();

            if (ent == null)
                return new NotFoundResult();

            var questionnaireResults = ent.ToModel(auth!.UserKey);

            return new OkObjectResult(questionnaireResults);
        }

        [Function("PostQuestionnaireResults_v1")]
        [OpenApiOperation(
            operationId: "postQuestionnaireResults",
            tags: ["questionnaire", "results"],
            Summary = "Store new questionnaire results")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiRequestBody("application/json", typeof(QuestionnaireResults), Description = "A questionnaire results", Required = true)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(QuestionnaireResults), Description = "Saved questionnaire results")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> PostQuestionnaireResults(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/questionnaire-results")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteChatHistory))
                return new UnauthorizedResult();

            var questionnaireResults = await req.ReadJson<QuestionnaireResults>();
            if (questionnaireResults == null)
                return new BadRequestResult();

            if (!Guid.TryParse(questionnaireResults.QuestionnaireId, out Guid questionnaireId))
                return new BadRequestResult();

            var entity = new QuestionnaireResultsEntity
            {
                UserId = auth!.User,
                Timestamp = DateTime.UtcNow,
                QuestionnaireId = questionnaireId
            };
            entity.SetQuestionnaireResults(auth!.UserKey, questionnaireResults);

            var add = await _db.QuestionnaireResults.AddAsync(entity);
            await add.Context.SaveChangesAsync();

            questionnaireResults.Id = entity.Id.ToString();
            return new OkObjectResult(questionnaireResults);
        }
    }
}
