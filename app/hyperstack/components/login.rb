class Login < HyperComponent

  render(DIV) do
    H1 { "Login" }

    if Rails.env == "production"
      HR{}
      H2 { "SSO providers" }
    else
      H2 { "SSO Doesn't work in development mode" }
    end
    
    HR{}
    H2 { "Local account" }
    FORM(method: "post") do
      csrf_token
      
      INPUT(type: :text, placeholder: "email", name: "email")
      BR{}
      INPUT(type: :password, placeholder: "password", name: "password")
      BR{}

      DIV {
        "Error logging in: #{@login_response}"
      } if @login_response
      
      BUTTON(action: "login", type: "submit") { "Login" }
    end

    if Hyperstack::Application.acting_user_id
      DIV { "You are logged in!"}

      FORM(method: "delete", ) do
        csrf_token
        BUTTON(action: "login", type: "submit") { "Logout" }
      end
    end
  end

  def csrf_token
    INPUT(type: "hidden", name: "authenticity_token",
          value: `$('meta[name="csrf-token"]')[0].content`)
  end
end
