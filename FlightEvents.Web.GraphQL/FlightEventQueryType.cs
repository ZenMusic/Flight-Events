﻿using FlightEvents.Data;
using HotChocolate;
using HotChocolate.Types;
using System.Collections.Generic;

namespace FlightEvents.Web.GraphQL
{
    public class FlightEventQueryType : ObjectType<FlightEvent>
    {
        protected override void Configure(IObjectTypeDescriptor<FlightEvent> descriptor)
        {
            descriptor.Field(o => o.FlightPlanIds).Ignore();
            descriptor.Field<FlightPlansResolver>(t => t.GetFlightPlans(default)).Type<ListType<FlightPlanQueryType>>();
        }
    }

    public class FlightPlansResolver
    {
        public IEnumerable<string> GetFlightPlans([Parent]FlightEvent flightEvent) => flightEvent.FlightPlanIds ?? new List<string>();
    }
}
