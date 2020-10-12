import React, { Component } from 'react';
import { Route } from 'react-router';
import { Home } from './components/Home';
import EventAdminPage from './components/EventAdminPage';
import StopwatchPage from './components/StopwatchPage';
import StreamOverlay from './components/StreamOverlay';

import './custom.css'

export default class App extends Component {
    static displayName = App.name;

    render() {
        return (
            <>
                <Route exact path='/' component={Home} />
                <Route exact path='/Events/:id/Admin' component={EventAdminPage} />
                <Route exact path='/Events/:id/Stopwatch' component={StopwatchPage} />
                <Route exact path='/Events/:id/StreamOverlay' component={StreamOverlay} />
            </>
        );
    }
}
