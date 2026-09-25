// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


namespace Aire.Memory;

public static class AireConstants
{
    public static class Queues
    {
        public const string UserDelete = "user-delete-queue";
        public const string DocumentEmbed = "document-embed";
    }

    public static class Blobs
    {
        public const string ChatLogs = "chatlogs";
        public const string Questionnaires = "questionnaires";
        public const string QuestionnaireResults = "questionnaire-results";
        public const string PublicQuestionnaireResults = "pub-questionnaire-results";
        public const string Contents = "contents";
        public const string Reminders = "reminders";
        public const string Documents = "documents";
    }

    public static class Tables
    {
        public const string Statistics = "Statistics";
    }

    public static class Limits
    {
        public const long ThumbnailSizeLimit = 2 * 1000 * 1000; // 2 MB limit
        public const long DocumentSizeLimit = 5 * 1000 * 1000; // 5 MB limit
        public const long ContentSizeLimit = 50 * 1000 * 1000; // 50 MB limit
    }
}
