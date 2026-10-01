(function (global) {
    "use strict";

    const emailNotificationId = "contoso.lead.emailFormat";
    const duplicateNotificationId = "contoso.lead.duplicateEmail";
    const priorityNotificationId = "contoso.lead.highPriority";
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    function validateEmail(executionContext) {
        const formContext = executionContext.getFormContext();
        const emailAttribute = formContext.getAttribute("emailaddress1");
        const emailControl = formContext.getControl("emailaddress1");
        const email = emailAttribute ? emailAttribute.getValue() : null;

        if (!email || emailPattern.test(email)) {
            if (emailControl) {
                emailControl.clearNotification(emailNotificationId);
            }
            return Boolean(email);
        }

        if (emailControl) {
            emailControl.setNotification("Enter a valid email address.", emailNotificationId);
        }
        return false;
    }

    async function checkDuplicateEmail(executionContext) {
        const formContext = executionContext.getFormContext();
        const emailAttribute = formContext.getAttribute("emailaddress1");
        const emailControl = formContext.getControl("emailaddress1");
        const email = emailAttribute ? emailAttribute.getValue() : null;

        if (!email || !emailPattern.test(email)) {
            if (emailControl) {
                emailControl.clearNotification(duplicateNotificationId);
            }
            return;
        }

        // OData string literals escape apostrophes by doubling them.
        const escapedEmail = email.replace(/'/g, "''");
        const currentLeadId = formContext.data.entity.getId().replace(/[{}]/g, "");
        let filter = `emailaddress1 eq '${escapedEmail}'`;
        if (currentLeadId) {
            filter += ` and leadid ne ${currentLeadId}`;
        }

        try {
            const query = `?$select=fullname&$filter=${filter}&$top=1`;
            const result = await global.Xrm.WebApi.retrieveMultipleRecords("lead", query);

            if (result.entities.length > 0) {
                const leadName = result.entities[0].fullname || "(unnamed lead)";
                if (emailControl) {
                    emailControl.setNotification(`Another lead already uses this email: ${leadName}.`, duplicateNotificationId);
                }
            } else if (emailControl) {
                emailControl.clearNotification(duplicateNotificationId);
            }
        } catch (error) {
            if (emailControl) {
                emailControl.clearNotification(duplicateNotificationId);
            }
            // Log lookup failures for administrators without blocking form use.
            if (global.console && global.console.error) {
                global.console.error("Unable to check for duplicate lead email.", error);
            }
        }
    }

    function updatePriorityNotification(executionContext) {
        const formContext = executionContext.getFormContext();
        const estimatedValue = formContext.getAttribute("estimatedvalue");
        const value = estimatedValue ? estimatedValue.getValue() : null;

        if (value > 500000) {
            formContext.ui.setFormNotification("High priority lead", "INFO", priorityNotificationId);
        } else {
            formContext.ui.clearFormNotification(priorityNotificationId);
        }
    }

    function onLoad(executionContext) {
        const formContext = executionContext.getFormContext();
        const emailAttribute = formContext.getAttribute("emailaddress1");
        const estimatedValueAttribute = formContext.getAttribute("estimatedvalue");

        if (emailAttribute) {
            emailAttribute.addOnChange(validateEmail);
            emailAttribute.addOnChange(checkDuplicateEmail);
            validateEmail(executionContext);
            checkDuplicateEmail(executionContext);
        }

        if (estimatedValueAttribute) {
            estimatedValueAttribute.addOnChange(updatePriorityNotification);
            updatePriorityNotification(executionContext);
        }
    }

    global.Contoso = global.Contoso || {};
    global.Contoso.Lead = global.Contoso.Lead || {};
    global.Contoso.Lead.onLoad = onLoad;
})(typeof window !== "undefined" ? window : globalThis);