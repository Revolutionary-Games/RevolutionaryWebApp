# frozen_string_literal: true

# Login page. This uses a rails controller for the actual login operation
class Login < HyperComponent
  before_mount do
    @email_entered = false
    @password_entered = false
    check_details
  end

  def check_details
    @details_entered = @email_entered && @password_entered
  end

  render(DIV) do
    H1 { 'Login' }

    if Rails.env == 'production'
      HR {}
      H2 { 'SSO providers' }

      P { 'Developer login' }

      H3 {
        RS.Form(method: 'post', action: '/login') {
          CSRF {}
          INPUT(type: :hidden, name: 'sso_type', value: 'devforum')
          RS.Button(type: 'submit', color: 'primary') {
            'Login Using a Development Forum Account'
          }
        }
      }

      HR {}
      P { 'Supporter (patron) login' }

      H3 {
        RS.Form(method: 'post', action: '/login') {
          CSRF {}
          INPUT(type: :hidden, name: 'sso_type', value: 'communityforum')
          RS.Button(type: 'submit', color: 'primary') {
            'Login Using a Community Forum Account'
          }
        }
      }

      H3 {
        RS.Form(method: 'post', action: '/login') {
          CSRF {}
          INPUT(type: :hidden, name: 'sso_type', value: 'patreon')
          RS.Button(type: 'submit', color: 'primary') {
            'Login Using Patreon'
          }
        }
      }

    else
      H2 { "SSO Doesn't work in development mode" }
    end

    HR {}
    H2 { 'Local account' }
    RS.Form(method: 'post', action: '/login') {
      CSRF {}
      RS.FormGroup {
        INPUT(type: :text, placeholder: 'email', name: 'email').on(:change) { |e|
          mutate {
            @email_entered = e.target.value != ''
            check_details
          }
        }
      }
      RS.FormGroup {
        INPUT(type: :password, placeholder: 'password', name: 'password').on(:change) { |e|
          mutate {
            @password_entered = e.target.value != ''
            check_details
          }
        }
      }

      RS.Button(color: 'primary', disabled: !@details_entered) { 'Login' }
    }

    if App.acting_user
      H3 { 'You are logged in!' }

      RS.Form(method: 'post', action: '/logout') {
        CSRF {}
        RS.Button(color: 'secondary') { 'Logout' }
      }
    end
  end
end
