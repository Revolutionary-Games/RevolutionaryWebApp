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
module Hyperstack
  def self.on_error(*args)
    ::Rails.logger.debug "[0;31;1mHYPERSTACK APPLICATION ERROR: 
"\
                         "To further investigate you may want to add a debugging "\
                         "breakpoint in config/initializers/hyperstack.rb[0;30;21m"
  end
end if Rails.env.development?
