﻿using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cogito.SqlServer.Deployment.Internal;

namespace Cogito.SqlServer.Deployment
{

    /// <summary>
    /// Ensures a subscription is in place at the publisher.
    /// </summary>
    public class SqlDeploymentPullSubscriptionStep : SqlDeploymentStep
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="instanceName"></param>
        /// <param name="databaseName"></param>
        public SqlDeploymentPullSubscriptionStep(string instanceName, string databaseName) :
            base(instanceName)
        {
            DatabaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
        }

        /// <summary>
        /// Gets the name of the subscriber database.
        /// </summary>
        public string DatabaseName { get; }

        /// <summary>
        /// Gets the name of the publisher instance on which the publication exists.
        /// </summary>
        public string PublisherInstanceName { get; internal set; }

        /// <summary>
        /// Gets the name of the publisher database.
        /// </summary>
        public string PublisherDatabaseName { get; internal set; }

        /// <summary>
        /// Gets the name of the publication.
        /// </summary>
        public string PublicationName { get; internal set; }

        public override async Task Execute(SqlDeploymentExecuteContext context, CancellationToken cancellationToken = default)
        {
            using var sub = await OpenConnectionAsync(cancellationToken);
            sub.ChangeDatabase(DatabaseName);

            using var pub = await OpenConnectionAsync(PublisherInstanceName, cancellationToken);
            pub.ChangeDatabase(PublisherDatabaseName);

            await pub.ExecuteNonQueryAsync($@"
                IF NOT EXISTS ( SELECT * FROM syssubscriptions WHERE srvname = {InstanceName} AND dest_db = {DatabaseName} )
                    EXEC sp_addsubscription
                        @publication = {PublicationName},
                        @subscriber = {InstanceName},
                        @destination_db = {DatabaseName},
                        @subscription_type = N'Pull',
                        @sync_type = N'automatic',
                        @article = N'all',
                        @update_mode = N'read only',
                        @subscriber_type = 0");

            var articles = (await pub.LoadDataTableAsync($@"
                SELECT      DISTINCT
                            NULLIF(a.artid, '')             as article_id,
                            NULLIF(a.name, '')              as article_name,
                            COALESCE(u.srvname, '')         as subscriber_name
                FROM        syspublications p
                INNER JOIN  sysarticles a
                    ON      a.pubid = p.pubid
                INNER JOIN  sys.tables t
                    ON      t.object_id = a.objid
                INNER JOIN  sys.schemas s
                    ON      s.schema_id = t.schema_id
                LEFT JOIN   syssubscriptions u
                    ON      u.artid = a.artid
                    AND     u.srvname = {InstanceName}
                WHERE       p.name = {PublicationName}"))
                .Rows.Cast<DataRow>()
                .Select(i => new
                {
                    ArticleId = (int)i["article_id"],
                    ArticleName = (string)i["article_name"],
                    SubscriberName = (string)i["subscriber_name"]
                });

            // add missing articles to the subscription
            foreach (var article in articles)
                if (!string.IsNullOrEmpty(article.ArticleName) && string.IsNullOrEmpty(article.SubscriberName))
                    await pub.ExecuteSpAddSubscriptionAsync(
                        publication: PublicationName,
                        subscriber: InstanceName,
                        subscriberType: 0,
                        subscriptionType: "Pull",
                        destinationDb: DatabaseName,
                        article: article.ArticleName,
                        syncType: "automatic",
                        updateMode: "read only");

            try
            {
                await sub.ExecuteSpAddPullSubscriptionAgentAsync(
                    publication: PublicationName,
                    subscriber: InstanceName,
                    subscriberDb: DatabaseName,
                    subscriberSecurityMode: 1);
            }
            catch (Exception e)
            {

            }

            // start publication
            try
            {
                await pub.ExecuteSpStartPublicationSnapshotAsync(PublicationName);
            }
            catch (Exception)
            {
                // ignore
            }
        }

    }

}
