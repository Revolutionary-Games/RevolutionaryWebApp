class UserProperties < HyperComponent
  param :user
  param :showToken, default: false

  render(DIV) do

    lookingAtSelf = @User == App.acting_user

    P { "email: #{ @User.email}" }
    P { "name: #{ @User.name}" }
    P { "admin: #{ @User.admin}" }
    P { "developer: #{ @User.developer}" }
    P { "sso_source: #{ @User.sso_source}" }
    P { "local: #{ @User.local}" }

    hasToken = @User.has_api_token
    
    P { "api token: #{@User.has_api_token}" }

    if hasToken == "true" && lookingAtSelf
      BUTTON { if !@ShowToken then 'View Token' else 'Hide Token' end }.on(:click){
        mutate @ShowToken = !@ShowToken
      }

      if @ShowToken
        P { "Your token is: #{@User.api_token}" }
      end

      BUTTON { "Clear Token" }.on(:click){
        @User.reset_api_token
      }
      
    elsif lookingAtSelf
      
      BUTTON { "Generate API Token"}.on(:click){

        GenerateAPITokenForUser.run
          .then {  }
          .fail { alert "failed to run operation" }
      }
    end
    
    P { "Git lfs token: #{ @User.has_lfs_token}" }

    if !lookingAtSelf
      H2 {"Actions"}
      BUTTON { 'DELETE' }.on(:click){}
    end



    
  end
end

class UserView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do

    user = User.find_by_id match.params[:id]

    if !user
      H1 { "No user exists with id = #{match.params[:id]}" }
      return
    end

    H1 { "User #{user.email} (#{user.id})" }

    H2 { "Properties" }

    UserProperties(user: user)

  end
end
