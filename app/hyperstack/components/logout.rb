# frozen_string_literal: true

# Page with a logout button that posts a form to the logout controller
class Logout < HyperComponent
  render(DIV) do
    H1 { 'Logout' }

    H1 { "You don't seem to be logged in" } unless App.acting_user

    HR {}
    H2 { 'If you are sure you want to logout press this:' }

    RS.Form(method: 'post', action: '/logout') do
      CSRF {}
      RS.Button(color: 'primary') { 'Logout' }
    end
  end
end
