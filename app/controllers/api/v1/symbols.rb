module API  
  module V1
    class Symbols < Grape::API
      include API::V1::Defaults

      resource :symbols do
        desc "Return all symbols"
        get "", root: :graduates do
          DebugSymbol.all
        end

        desc "Return a symbol"
        params do
          requires :id, type: String, desc: "ID of the 
            symbol"
        end
        get ":id", root: "symbol" do
          DebugSymbol.where(id: permitted_params[:id]).first!
        end
      end
    end
  end
end
