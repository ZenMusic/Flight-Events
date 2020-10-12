import * as React from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { Query } from '@apollo/client/react/components';
import { ApolloQueryResult } from '@apollo/client/core';
import { gql } from '@apollo/client';
import { RouteComponentProps } from 'react-router-dom';
import { Container, Row, Col, Button } from 'reactstrap';
import { FlightEvent } from '../Models';

interface RouteProps {
    id: string;
}

const hub = new HubConnectionBuilder()
    .withUrl('/FlightEventHub')
    .withAutomaticReconnect()
    .build();

export default (props: RouteComponentProps<RouteProps>) => {
    React.useEffect(() => {
        (async () => {
            hub.onreconnected(async connectionId => {
                console.log('Connected to SignalR with connection ID ' + connectionId);
            });

            await hub.start();
        })();
    }, []);

    const startRace = () => {
        hub.send('StartRace', props.match.params.id);
    }

    const stopRace = () => {
        hub.send('StopRace', props.match.params.id);
    }

    return (
        <Container>
            <Row>
                <Col>
                    <Query query={gql`
query getEvent($id: Uuid!) {
    flightEvent(id: $id) {
        id
        type
        name
        description
        racerCallsigns
        markedWaypoints
    }
}
`} variables={{ id: props.match.params.id }}>{({ loading, error, data }: ApolloQueryResult<{ flightEvent: FlightEvent }>) => {
                            if (loading) return <>Loading...</>
                            if (error) return <>Error!</>

                            const event = data.flightEvent;

                            return <>
                                <h2>Event {event.name}</h2>

                                <p>{event.description}</p>

                                {event.type === 'RACE' && <>
                                    {event.racerCallsigns && <>
                                        <h6>Racers</h6>
                                        <ul>
                                            {event.racerCallsigns.map(callsign => <li>{callsign}</li>)}
                                        </ul>
                                    </>}

                                    {event.markedWaypoints && <>
                                        <h6>Checkpoints</h6>
                                        <ul>
                                            {event.markedWaypoints.map(waypoint => <li>{waypoint}</li>)}
                                        </ul>
                                    </>}

                                    {event.racerCallsigns && !!event.racerCallsigns.length &&
                                        event.markedWaypoints && !!event.markedWaypoints.length ? <>
                                        <Button color="primary" onClick={startRace}>Start Race</Button>
                                        <Button color="warning" onClick={stopRace}>Stop Race</Button>
                                    </> : <p>Racers and Checkpoints need to be setup before starting the race.</p>}
                                </>}
                            </>
                        }}</Query>
                </Col>
            </Row>
        </Container>
    )
}