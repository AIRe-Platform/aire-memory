using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth.Models;
using Aire.Sdk.Auth.Roles;
using Aire.Sdk.Auth.Scopes;
using Aire.Sdk.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Aire.Memory.Api
{
    public class Questionnaire_v1
    {
        private readonly DatabaseContext _db;
        private readonly IJwtTokenService _jwt;
        private readonly ILogger _log;

        public Questionnaire_v1(DatabaseContext db, IJwtTokenService jwt, ILoggerFactory loggerFactory)
        {
            _db = db;
            _jwt = jwt;
            _log = loggerFactory.CreateLogger<Questionnaire_v1>();
        }

        [Function("GetQuestionnaire_v1")]
        [OpenApiOperation(
            operationId: "getQuestionnaire",
            tags: ["questionnaire"],
            Summary = "Get a list of questionnaires")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> GetQuestionnaires(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            var list = await _db.Questionnaires
                .ToListAsync();

            var questionnaires = new List<Questionnaire>();
            foreach (var item in list)
            {
                questionnaires.Add(new Questionnaire(item));
            }

            return new ObjectResult(questionnaires);
        }

        [Function("GetQuestionnaireWithId_v1")]
        [OpenApiOperation(
            operationId: "getQuestionnaireWithId",
            tags: ["questionnaire"],
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
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> GetQuestionnaireWithId(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/questionnaire/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            if (!Guid.TryParse(id, out Guid questionnaireId))
                return new BadRequestResult();

            var ent = await _db.Questionnaires
                .Where(x => x.Id == questionnaireId)
                .FirstOrDefaultAsync();

            if(ent == null)
                return new NotFoundResult();

            var questionnaire = new Questionnaire(ent);

            return new ObjectResult(questionnaire);
        }

        [Function("PostQuestionnaire_v1")]
        [OpenApiOperation(
            operationId: "postQuestionnaire",
            tags: ["questionnaire"],
            Summary = "Store new questionnaire")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLogMetadata), Description = "Saved questionnaire")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> PostQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.WriteQuestionnaire))
                return new UnauthorizedResult();

            var questionnaire = await req.ReadJson<Questionnaire>();
            if (questionnaire == null)
                return new BadRequestResult();

            var entity = new QuestionnaireEntity(questionnaire);

            var add = await _db.Questionnaires.AddAsync(entity);
            await add.Context.SaveChangesAsync();

            return new ObjectResult(questionnaire);
        }
    }
}
