using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Contoso.Plugins
{
    /// <summary>
    /// Applies lead rating rules during the synchronous PreOperation pipeline stage.
    /// Register for Create and Update of lead, PreOperation, synchronous, with a
    /// PreImage named "PreImage" on Update that includes estimatedvalue and emailaddress1.
    /// </summary>
    public sealed class LeadRatingPlugin : IPlugin
    {
        private const string LeadEntityName = "lead";
        private const string PreImageName = "PreImage";

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context == null || tracingService == null)
            {
                throw new InvalidPluginExecutionException("The plugin execution context or tracing service is unavailable.");
            }

            tracingService.Trace("LeadRatingPlugin started. Message: {0}, Depth: {1}.", context.MessageName, context.Depth);

            // A deeper invocation usually indicates a plugin loop; do not process it again.
            if (context.Depth > 1)
            {
                tracingService.Trace("Skipping execution because the plugin depth is greater than one.");
                return;
            }

            if (context.Stage != 20 || context.Mode != 0 ||
                (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)))
            {
                tracingService.Trace("Skipping execution outside synchronous PreOperation Create/Update.");
                return;
            }

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                tracingService.Trace("Skipping execution because Target is missing or is not an Entity.");
                return;
            }

            var target = (Entity)context.InputParameters["Target"];
            if (!string.Equals(target.LogicalName, LeadEntityName, StringComparison.OrdinalIgnoreCase))
            {
                tracingService.Trace("Skipping execution for entity {0}.", target.LogicalName);
                return;
            }

            Entity preImage = null;
            if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.PreEntityImages.Contains(PreImageName))
                {
                    tracingService.Trace("The required PreImage is missing.");
                    throw new InvalidPluginExecutionException(
                        "LeadRatingPlugin requires an Update pre-image named 'PreImage' containing estimatedvalue and emailaddress1.");
                }

                preImage = context.PreEntityImages[PreImageName];
            }

            var estimatedValue = GetAttributeValue<Money>(target, preImage, "estimatedvalue");
            if (estimatedValue != null && estimatedValue.Value > 500000m)
            {
                target["leadqualitycode"] = new OptionSetValue(1);
                tracingService.Trace("Set leadqualitycode to Hot because estimatedvalue is greater than 500000.");
            }

            var email = GetAttributeValue<string>(target, preImage, "emailaddress1");
            if (!string.IsNullOrWhiteSpace(email))
            {
                EnsureEmailIsUnique(serviceProvider, tracingService, target, context, email);
            }

            tracingService.Trace("LeadRatingPlugin completed successfully.");
        }

        private static T GetAttributeValue<T>(Entity target, Entity preImage, string attributeName)
        {
            // Target takes precedence, including an explicitly cleared (null) value.
            if (target.Attributes.Contains(attributeName))
            {
                return target.GetAttributeValue<T>(attributeName);
            }

            return preImage == null ? default(T) : preImage.GetAttributeValue<T>(attributeName);
        }

        private static void EnsureEmailIsUnique(
            IServiceProvider serviceProvider,
            ITracingService tracingService,
            Entity target,
            IPluginExecutionContext context,
            string email)
        {
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            if (serviceFactory == null)
            {
                throw new InvalidPluginExecutionException("The organization service factory is unavailable.");
            }

            var organizationService = serviceFactory.CreateOrganizationService(context.UserId);
            var query = new QueryExpression(LeadEntityName)
            {
                ColumnSet = new ColumnSet("fullname"),
                TopCount = 1
            };
            query.Criteria.AddCondition("emailaddress1", ConditionOperator.Equal, email);

            var currentLeadId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            if (currentLeadId != Guid.Empty)
            {
                query.Criteria.AddCondition("leadid", ConditionOperator.NotEqual, currentLeadId);
            }

            tracingService.Trace("Checking whether another lead uses emailaddress1.");
            var matchingLeads = organizationService.RetrieveMultiple(query);
            if (matchingLeads.Entities.Count > 0)
            {
                var duplicateName = matchingLeads.Entities[0].GetAttributeValue<string>("fullname");
                var message = string.IsNullOrWhiteSpace(duplicateName)
                    ? "Another lead already uses this email address."
                    : string.Format("Another lead ({0}) already uses this email address.", duplicateName);

                tracingService.Trace("Duplicate email found. Rejecting the operation.");
                throw new InvalidPluginExecutionException(message);
            }
        }
    }
}