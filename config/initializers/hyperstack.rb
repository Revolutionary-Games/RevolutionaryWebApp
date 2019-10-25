# frozen_string_literal: true

# config/initializers/hyperstack.rb
# If you are not using ActionCable, see http://hyperstack.orgs/docs/models/configuring-transport/
Hyperstack.configuration do |config|
  config.transport = :action_cable
  config.prerendering = :off # or :on
  config.cancel_import 'react/react-source-browser' # bring your own React and ReactRouter via Yarn/Webpacker
  config.import 'hyperstack/component/jquery', client_only: true # remove this line if you don't need jquery
  config.import 'hyperstack/hotloader', client_only: true if Rails.env.development?
end

# useful for debugging
if Rails.env.development?
  module Hyperstack
    def self.on_error(_operation, _err, _params, formatted_error_message)
      ::Rails.logger.debug(
        "#{formatted_error_message}\n\n" +
        Pastel.new.red(
          'To further investigate you may want to add a debugging '\
          'breakpoint to the on_error method in config/initializers/hyperstack.rb'
        )
      )
    end
  end
end
