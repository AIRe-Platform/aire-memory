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
using System.Diagnostics;

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
        public async Task<IActionResult> GetQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var list = await _db.Questionnaires
                .ToListAsync();
            
            var questionnaires = new List<Questionnaire>();
            foreach (var item in list)
            {
                questionnaires.Add(new Questionnaire(item));
            }

            return new ObjectResult(questionnaires);
        }

        [Function("PostQuestionnaire_v1")]
        public async Task<IActionResult> PostQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var questionnaire = await req.ReadJson<Questionnaire>();
            if(questionnaire == null)
                return new BadRequestResult();

            var entity = new QuestionnaireEntity(questionnaire);

            var add = await _db.Questionnaires.AddAsync(entity);
            await add.Context.SaveChangesAsync();

            return new ObjectResult(questionnaire);
        }
    }
}
