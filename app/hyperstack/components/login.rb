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
    FORM do
      # LABEL{ "email" }
      INPUT(type: :text, placeholder: "email"){
      }
      BR{}
      # LABEL{ "password" }
      INPUT(type: :password, placeholder: "password"){
      }
      BR{}

      DIV {
        "Error logging in: #{@login_response}"
      } if @login_response
      
      BUTTON(action: "local_login") { "Login" }
    end
  end
end
