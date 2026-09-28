// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: MIM custom action activity "Actor Inheritance". Keeps the actor
//          attributes of sub-identities in line with their main identity.
//
//          Direction is derived from the request target:
//            1. Target references a main identity (it is a sub-identity):
//               the target pulls the actor values from its main identity.
//            2. In every case the effective values (the main identity's values
//               when the target is a sub-identity, otherwise the target's own)
//               are pushed to all sub-identities that reference the target.
//
//          Only real differences are written. Re-running the activity is
//          harmless, and cycles or chains of identities settle by themselves.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Workflow.Activities;
using System.Workflow.ComponentModel;
using Microsoft.ResourceManagement.WebServices.WSResourceManagement;
using Microsoft.ResourceManagement.Workflow.Activities;

namespace MIM.ActorInheritance.Workflow
{
    /// <summary>
    /// MIM action activity that makes sub-identities inherit the actor attributes of their main identity.
    /// </summary>
    public class ActorInheritanceActivity : SequenceActivity
    {
        public const string DefaultIdentityObjectType = "Person";

        public const string DefaultMainIdentityAttribute = "MainIdentity";

        public static readonly DependencyProperty IdentityObjectTypeProperty = DependencyProperty.Register(
            "IdentityObjectType",
            typeof(string),
            typeof(ActorInheritanceActivity),
            new PropertyMetadata(DefaultIdentityObjectType));

        public static readonly DependencyProperty MainIdentityAttributeProperty = DependencyProperty.Register(
            "MainIdentityAttribute",
            typeof(string),
            typeof(ActorInheritanceActivity),
            new PropertyMetadata(DefaultMainIdentityAttribute));

        public static readonly DependencyProperty InheritedAttributesProperty = DependencyProperty.Register(
            "InheritedAttributes",
            typeof(string),
            typeof(ActorInheritanceActivity));

        /// <summary>
        /// ObjectID of the built-in "Forefront Identity Manager Service Account". Reads and updates run
        /// as this account, so the activity does not depend on the requestor's permissions.
        /// </summary>
        private static readonly Guid FimServiceAccountId = new Guid("e05d1f1b-3d5e-4014-baa6-94dee7d68c89");

        private static readonly TraceSource Tracer = new TraceSource("MIM.ActorInheritance");

        private CodeActivity prepare;
        private ReadResourceActivity readTarget;
        private CodeActivity analyzeTarget;
        private IfElseActivity ifTargetHasMainIdentity;
        private IfElseBranchActivity targetHasMainIdentity;
        private ReadResourceActivity readMainIdentity;
        private CodeActivity calculateTarget;
        private EnumerateResourcesActivity findSubIdentities;
        private SequenceActivity subIdentityIteration;
        private CodeActivity calculateSubIdentity;
        private IfElseActivity ifUpdatesPending;
        private IfElseBranchActivity updatesPending;
        private ReplicatorActivity applyUpdates;
        private UpdateResourceActivity updateResource;
        private CodeActivity complete;

        // Runtime state. All types are serializable because the MIM Service may persist the workflow instance.
        private List<string> attributes;
        private Guid targetId;
        private Guid mainIdentityId;
        private Dictionary<string, object> effectiveValues;
        private List<PendingUpdate> pendingUpdates;
        private int subIdentityCount;

        public ActorInheritanceActivity()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Object type of main and sub-identities (default: Person).
        /// </summary>
        [Description("Object type of main and sub-identities.")]
        [Category("Settings")]
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string IdentityObjectType
        {
            get { return (string)this.GetValue(IdentityObjectTypeProperty); }
            set { this.SetValue(IdentityObjectTypeProperty, value); }
        }

        /// <summary>
        /// Single-valued reference attribute on a sub-identity that points to its main identity.
        /// </summary>
        [Description("Reference attribute on the sub-identity that points to the main identity.")]
        [Category("Settings")]
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string MainIdentityAttribute
        {
            get { return (string)this.GetValue(MainIdentityAttributeProperty); }
            set { this.SetValue(MainIdentityAttributeProperty, value); }
        }

        /// <summary>
        /// Actor attributes copied from the main identity to its sub-identities, separated by ';'.
        /// </summary>
        [Description("Actor attributes inherited by sub-identities, separated by ';'.")]
        [Category("Settings")]
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string InheritedAttributes
        {
            get { return (string)this.GetValue(InheritedAttributesProperty); }
            set { this.SetValue(InheritedAttributesProperty, value); }
        }

        private void Prepare_ExecuteCode(object sender, EventArgs e)
        {
            this.attributes = new List<string>(InheritanceCalculator.ParseAttributeList(this.InheritedAttributes));
            IList<string> errors = InheritanceCalculator.ValidateConfiguration(this.IdentityObjectType, this.MainIdentityAttribute, this.attributes);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("The actor inheritance activity is misconfigured: " + string.Join(" ", errors));
            }

            SequentialWorkflow workflow;
            if (!SequentialWorkflow.TryGetContainingWorkflow(this, out workflow))
            {
                throw new InvalidOperationException("The actor inheritance activity must run inside a MIM workflow.");
            }

            this.targetId = workflow.TargetId;
            this.mainIdentityId = Guid.Empty;
            this.effectiveValues = null;
            this.pendingUpdates = new List<PendingUpdate>();
            this.subIdentityCount = 0;

            List<string> selection = new List<string>(this.attributes) { this.MainIdentityAttribute };
            this.readTarget.ActorId = FimServiceAccountId;
            this.readTarget.ResourceId = this.targetId;
            this.readTarget.SelectionAttributes = selection.ToArray();
        }

        private void AnalyzeTarget_ExecuteCode(object sender, EventArgs e)
        {
            if (this.readTarget.Resource == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Target resource '{0}' could not be read.", this.targetId));
            }

            Guid referencedMainIdentity = ResourceValues.GetReference(this.readTarget.Resource, this.MainIdentityAttribute);
            if (referencedMainIdentity == Guid.Empty || referencedMainIdentity == this.targetId)
            {
                return;
            }

            this.mainIdentityId = referencedMainIdentity;
            this.readMainIdentity.ActorId = FimServiceAccountId;
            this.readMainIdentity.ResourceId = referencedMainIdentity;
            this.readMainIdentity.SelectionAttributes = this.attributes.ToArray();
        }

        private void TargetHasMainIdentity_Condition(object sender, ConditionalEventArgs e)
        {
            e.Result = this.mainIdentityId != Guid.Empty;
        }

        private void CalculateTarget_ExecuteCode(object sender, EventArgs e)
        {
            Dictionary<string, object> targetValues = ResourceValues.GetAll(this.readTarget.Resource, this.attributes);

            if (this.mainIdentityId != Guid.Empty)
            {
                // The target is a sub-identity: it inherits from its main identity ...
                this.effectiveValues = ResourceValues.GetAll(this.readMainIdentity.Resource, this.attributes);
                this.QueueUpdate(this.targetId, targetValues);
            }
            else
            {
                // ... otherwise the target is a main identity and its own values are authoritative.
                this.effectiveValues = targetValues;
            }

            // In both cases the effective values are pushed to the sub-identities of the target.
            List<string> selection = new List<string>(this.attributes) { "ObjectID" };
            this.findSubIdentities.ActorId = FimServiceAccountId;
            this.findSubIdentities.XPathFilter = string.Format(
                CultureInfo.InvariantCulture,
                "/{0}[{1}='{2}']",
                this.IdentityObjectType,
                this.MainIdentityAttribute,
                this.targetId);
            this.findSubIdentities.Selection = selection.ToArray();
        }

        private void CalculateSubIdentity_ExecuteCode(object sender, EventArgs e)
        {
            // The code activity runs in the execution context spawned for the current enumeration item.
            // Its parent is the iteration sequence, i.e. the direct child of the enumerate activity.
            Activity codeActivity = (Activity)sender;
            ResourceType subIdentity = EnumerateResourcesActivity.GetCurrentIterationItem(codeActivity.Parent) as ResourceType;
            if (subIdentity == null)
            {
                return;
            }

            object objectId = ResourceValues.Get(subIdentity, "ObjectID");
            if (!(objectId is Guid) || (Guid)objectId == this.targetId)
            {
                return;
            }

            this.subIdentityCount++;
            this.QueueUpdate((Guid)objectId, ResourceValues.GetAll(subIdentity, this.attributes));
        }

        private void UpdatesPending_Condition(object sender, ConditionalEventArgs e)
        {
            e.Result = this.pendingUpdates != null && this.pendingUpdates.Count > 0;
        }

        private void ApplyUpdates_Initialized(object sender, EventArgs e)
        {
            ((ReplicatorActivity)sender).InitialChildData = this.pendingUpdates;
        }

        private void ApplyUpdates_ChildInitialized(object sender, ReplicatorChildEventArgs e)
        {
            PendingUpdate pending = (PendingUpdate)e.InstanceData;
            UpdateResourceActivity update = (UpdateResourceActivity)e.Activity;

            update.ActorId = FimServiceAccountId;
            update.ResourceId = pending.ResourceId;
            update.ApplyAuthorizationPolicy = false;
            update.UpdateParameters = ResourceValues.ToUpdateParameters(pending.Changes);

            Tracer.TraceEvent(
                TraceEventType.Information,
                0,
                "Actor inheritance: updating {0}: {1}",
                pending.ResourceId,
                string.Join("; ", pending.Changes.Select(change => change.ToString())));
        }

        private void Complete_ExecuteCode(object sender, EventArgs e)
        {
            Tracer.TraceEvent(
                TraceEventType.Information,
                0,
                "Actor inheritance: target {0}, main identity {1}, {2} sub-identities checked, {3} resources updated.",
                this.targetId,
                this.mainIdentityId == Guid.Empty ? "(none)" : this.mainIdentityId.ToString(),
                this.subIdentityCount,
                this.pendingUpdates.Count);
        }

        private void QueueUpdate(Guid resourceId, IDictionary<string, object> currentValues)
        {
            IList<AttributeChange> changes = InheritanceCalculator.CalculateChanges(this.attributes, this.effectiveValues, currentValues);
            if (changes.Count > 0)
            {
                this.pendingUpdates.Add(new PendingUpdate(resourceId, changes));
            }
        }

        private void InitializeComponent()
        {
            this.CanModifyActivities = true;

            this.prepare = new CodeActivity { Name = "Prepare" };
            this.prepare.ExecuteCode += new EventHandler(this.Prepare_ExecuteCode);

            this.readTarget = new ReadResourceActivity { Name = "ReadTarget" };

            this.analyzeTarget = new CodeActivity { Name = "AnalyzeTarget" };
            this.analyzeTarget.ExecuteCode += new EventHandler(this.AnalyzeTarget_ExecuteCode);

            // Read the main identity only when the target is a sub-identity.
            this.readMainIdentity = new ReadResourceActivity { Name = "ReadMainIdentity" };
            CodeCondition hasMainIdentity = new CodeCondition();
            hasMainIdentity.Condition += new EventHandler<ConditionalEventArgs>(this.TargetHasMainIdentity_Condition);
            this.targetHasMainIdentity = new IfElseBranchActivity { Name = "TargetHasMainIdentity", Condition = hasMainIdentity };
            this.targetHasMainIdentity.Activities.Add(this.readMainIdentity);
            this.ifTargetHasMainIdentity = new IfElseActivity { Name = "IfTargetHasMainIdentity" };
            this.ifTargetHasMainIdentity.Activities.Add(this.targetHasMainIdentity);

            this.calculateTarget = new CodeActivity { Name = "CalculateTarget" };
            this.calculateTarget.ExecuteCode += new EventHandler(this.CalculateTarget_ExecuteCode);

            // Enumerate the sub-identities of the target and calculate their changes one by one.
            this.calculateSubIdentity = new CodeActivity { Name = "CalculateSubIdentity" };
            this.calculateSubIdentity.ExecuteCode += new EventHandler(this.CalculateSubIdentity_ExecuteCode);
            this.subIdentityIteration = new SequenceActivity { Name = "SubIdentityIteration" };
            this.subIdentityIteration.Activities.Add(this.calculateSubIdentity);
            this.findSubIdentities = new EnumerateResourcesActivity { Name = "FindSubIdentities", PageSize = 100 };
            this.findSubIdentities.Activities.Add(this.subIdentityIteration);

            // Write all collected changes, one update request per resource.
            this.updateResource = new UpdateResourceActivity { Name = "UpdateResource" };
            this.applyUpdates = new ReplicatorActivity { Name = "ApplyUpdates", ExecutionType = ExecutionType.Sequence };
            this.applyUpdates.Initialized += new EventHandler(this.ApplyUpdates_Initialized);
            this.applyUpdates.ChildInitialized += new EventHandler<ReplicatorChildEventArgs>(this.ApplyUpdates_ChildInitialized);
            this.applyUpdates.Activities.Add(this.updateResource);
            CodeCondition hasUpdates = new CodeCondition();
            hasUpdates.Condition += new EventHandler<ConditionalEventArgs>(this.UpdatesPending_Condition);
            this.updatesPending = new IfElseBranchActivity { Name = "UpdatesPending", Condition = hasUpdates };
            this.updatesPending.Activities.Add(this.applyUpdates);
            this.ifUpdatesPending = new IfElseActivity { Name = "IfUpdatesPending" };
            this.ifUpdatesPending.Activities.Add(this.updatesPending);

            this.complete = new CodeActivity { Name = "Complete" };
            this.complete.ExecuteCode += new EventHandler(this.Complete_ExecuteCode);

            this.Activities.Add(this.prepare);
            this.Activities.Add(this.readTarget);
            this.Activities.Add(this.analyzeTarget);
            this.Activities.Add(this.ifTargetHasMainIdentity);
            this.Activities.Add(this.calculateTarget);
            this.Activities.Add(this.findSubIdentities);
            this.Activities.Add(this.ifUpdatesPending);
            this.Activities.Add(this.complete);
            this.Name = "ActorInheritanceActivity";

            this.CanModifyActivities = false;
        }
    }
}
