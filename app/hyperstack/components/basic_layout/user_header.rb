# frozen_string_literal: true

# Shows logged in user info
class UserHeader < HyperComponent
  include Hyperstack::Router::Helpers

  render do
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
                          'a user'
                        end
        }
        HR {}
      end
    end
  end
end
