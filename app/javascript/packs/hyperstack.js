// Import all the modules
import React from 'react';
import ReactDOM from 'react-dom';

import * as ReactStrap from 'reactstrap';

// for opal/hyperstack modules to find React and others they must explicitly be saved
// to the global space, otherwise webpack will encapsulate them locally here
global.React = React;
global.ReactDOM = ReactDOM;
global.ReactStrap = ReactStrap;
global.ReactStrap.default = ReactStrap;
// A shorthand name for ReactStrap
global.RS = ReactStrap;
global.RS.default = ReactStrap;
