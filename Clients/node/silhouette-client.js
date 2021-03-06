﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
/*
** Create a Silhouette client using the specified transport
** mqtt
** iothub
** ...
*/

function create(transport, config)
{
  switch (transport) {
    case 'mqtt':
      return require('./silhouette-client-mqtt').create(config);
    case 'iothub':
      return require('./silhouette-client-iothub').create(config);
    default:
      // Raise some kind of error
  }
}

module.exports.create = create;

