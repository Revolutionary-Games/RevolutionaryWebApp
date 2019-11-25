# frozen_string_literal: true

# Shows logged in user info
class UserHeader < HyperComponent
  include Hyperstack::Router::Helpers
  # include Hyperstack::Component::WhileLoading

  render do
    # if resources_loaded?
    if App.acting_user
      DIV(class: 'container') do
        SPAN { 'Welcome ' }
        NavLink('/me') { App.acting_user.email }
        SPAN {
          ' You are ' + if App.acting_user.admin?
                          'an admin'
                        elsif App.acting_user.developer?
                          'a developer'
                        else
                          'an user'
                        end
        }
        HR {}
      end
    end
    # end
  end
end
