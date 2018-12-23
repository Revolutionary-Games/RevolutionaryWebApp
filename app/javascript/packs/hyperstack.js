// Import all the modules
import React from 'react';
import ReactDOM from 'react-dom';

// for opal/hyperstack modules to find React and others they must explicitly be saved
// to the global space, otherwise webpack will encapsulate them locally here
global.React = React;
global.ReactDOM = ReactDOM;
